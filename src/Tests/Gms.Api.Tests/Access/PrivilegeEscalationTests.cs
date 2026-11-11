using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Access;
using Gms.Shared.Problems;
using Gms.Shared.Security;

namespace Gms.Api.Tests.Access;

/// <summary>SEC-001: pemegang izin pengelolaan pengguna tidak boleh menjadi atau mengambil alih Administrator.</summary>
[Collection(ApiCollection.Name)]
public sealed class PrivilegeEscalationTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    /// <summary>
    /// Permission AccessManagement.* kini hanya untuk Administrator (tidak dapat diberikan lewat API role). Pertahanan berlapis ini
    /// harus tetap berlaku bila izin tersebut sampai ke role lain (mis. perubahan katalog di masa depan atau suntingan data langsung),
    /// maka izin disisipkan langsung ke database.
    /// </summary>
    private async Task<TestUser> UserAdminAsync()
    {
        var role = await H.CreateRoleAsync();
        string[] keys =
        [
            Permissions.AccessManagement.User.View, Permissions.AccessManagement.User.Create, Permissions.AccessManagement.User.Edit,
            Permissions.AccessManagement.User.Deactivate, Permissions.AccessManagement.User.ResetPassword,
            Permissions.AccessManagement.User.Unlock, Permissions.AccessManagement.User.EndSession,
        ];
        await using (var db = H.CreateDbContext())
        {
            db.RolePermissions.AddRange(keys.Select(k => new Gms.Domain.Access.RolePermission { RoleId = role.Id, PermissionKey = k }));
            await db.SaveChangesAsync();
        }

        return await H.CreateReadyUserAsync(role.Id);
    }

    private async Task<UserDetail> UserAsync(Guid id) => (await H.Api.GetAsync($"/api/v1/users/{id}", H.AdminToken)).Ok().As<UserDetail>();

    [Fact]
    public async Task Non_Administrator_tidak_dapat_membuat_pengguna_ber_role_Administrator()
    {
        var actor = await UserAdminAsync();

        var response = await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(TestHost.UniqueName(), "Calon Admin", H.AdminRoleId, TestHost.DefaultPassword), actor.Token);

        ProblemAssert.Is(response, HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    [Fact]
    public async Task Non_Administrator_tidak_dapat_menaikkan_role_pengguna_lain_atau_dirinya_menjadi_Administrator()
    {
        var actor = await UserAdminAsync();
        var plainRole = await H.CreateRoleAsync();
        var other = await H.CreateUserAsync(plainRole.Id);

        var otherDetail = await UserAsync(other.Id);
        var promoteOther = await H.Api.PutAsync($"/api/v1/users/{other.Id}",
            new UpdateUserRequest(otherDetail.FullName, H.AdminRoleId, otherDetail.RowVersion), actor.Token);
        ProblemAssert.Is(promoteOther, HttpStatusCode.Forbidden, ProblemTypes.Forbidden);

        var self = await UserAsync(actor.Id);
        var promoteSelf = await H.Api.PutAsync($"/api/v1/users/{actor.Id}",
            new UpdateUserRequest(self.FullName, H.AdminRoleId, self.RowVersion), actor.Token);
        ProblemAssert.Is(promoteSelf, HttpStatusCode.Forbidden, ProblemTypes.Forbidden);

        Assert.Equal(plainRole.Id, (await UserAsync(other.Id)).RoleId);
        Assert.Equal(self.RoleId, (await UserAsync(actor.Id)).RoleId);
    }

    [Fact]
    public async Task Non_Administrator_tidak_dapat_mengubah_role_dirinya_ke_role_lain()
    {
        var actor = await UserAdminAsync();
        var stronger = await H.CreateRoleAsync();
        var self = await UserAsync(actor.Id);

        var response = await H.Api.PutAsync($"/api/v1/users/{actor.Id}",
            new UpdateUserRequest(self.FullName, stronger.Id, self.RowVersion), actor.Token);

        ProblemAssert.Is(response, HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    [Fact]
    public async Task Non_Administrator_tidak_dapat_mengelola_pengguna_ber_role_Administrator()
    {
        var actor = await UserAdminAsync();
        var admin = await H.CreateReadyAdminAsync();
        var detail = await UserAsync(admin.Id);

        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/users/{admin.Id}/reset-password", new ResetPasswordRequest(null), actor.Token),
            HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PutAsync($"/api/v1/users/{admin.Id}", new UpdateUserRequest("Diubah", detail.RoleId, detail.RowVersion), actor.Token),
            HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/users/{admin.Id}/unlock", null, actor.Token),
            HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/users/{admin.Id}/end-session", null, actor.Token),
            HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/users/{admin.Id}/deactivate?rowVersion={detail.RowVersion}", null, actor.Token),
            HttpStatusCode.Forbidden, ProblemTypes.Forbidden);

        // Sesi dan password Administrator tidak tersentuh.
        Assert.Equal(HttpStatusCode.OK, (await H.Api.GetAsync("/api/v1/auth/me", admin.Token)).Status);
        await H.LoginAsync(admin.UserName, admin.Password);
    }

    [Fact]
    public async Task Non_Administrator_tetap_dapat_mengelola_pengguna_biasa()
    {
        var actor = await UserAdminAsync();
        var plainRole = await H.CreateRoleAsync();

        var created = await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(TestHost.UniqueName(), "Biasa", plainRole.Id, TestHost.DefaultPassword), actor.Token);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var id = created.As<UserDetail>().Id;

        (await H.Api.PostAsync($"/api/v1/users/{id}/reset-password", new ResetPasswordRequest(null), actor.Token)).Ok();
        var detail = await UserAsync(id);
        (await H.Api.PutAsync($"/api/v1/users/{id}", new UpdateUserRequest("Nama Baru", plainRole.Id, detail.RowVersion), actor.Token)).Ok();
    }

    [Fact]
    public async Task Administrator_tetap_dapat_membuat_dan_mempromosikan_Administrator()
    {
        var admin = await H.CreateReadyAdminAsync();
        var plainRole = await H.CreateRoleAsync();
        var user = await H.CreateUserAsync(plainRole.Id);
        var detail = await UserAsync(user.Id);

        var create = await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(TestHost.UniqueName(), "Admin Baru", H.AdminRoleId, TestHost.DefaultPassword), admin.Token);
        Assert.Equal(HttpStatusCode.Created, create.Status);

        (await H.Api.PutAsync($"/api/v1/users/{user.Id}",
            new UpdateUserRequest(detail.FullName, H.AdminRoleId, detail.RowVersion), admin.Token)).Ok();
    }
}
