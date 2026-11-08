using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Access;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Tests.Access;

[Collection(ApiCollection.Name)]
public sealed class RoleTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private Task<ApiResponse> SaveAsync(SaveRoleRequest request) => H.Api.PostAsync("/api/v1/roles", request, H.AdminToken);

    [Fact]
    public async Task Buat_role_dengan_permission_valid_dan_detail_terurut()
    {
        var created = await H.CreateRoleAsync(null,
            Permissions.WorkOrder.View, Permissions.WorkOrder.Release, Permissions.CustomerPO.View);

        var detail = (await H.Api.GetAsync($"/api/v1/roles/{created.Id}", H.AdminToken)).Ok().As<RoleDetail>();
        Assert.False(detail.IsSystem);
        Assert.Equal(0, detail.UserCount);
        Assert.Equal(
            [Permissions.CustomerPO.View, Permissions.WorkOrder.Release, Permissions.WorkOrder.View],
            detail.PermissionKeys);
    }

    [Fact]
    public async Task Aksi_tanpa_View_pada_resource_yang_sama_ditolak()
    {
        var response = await SaveAsync(new SaveRoleRequest(TestHost.UniqueName("Role"), null, [Permissions.WorkOrder.Release]));

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(problem.Errors!["permissionKeys"], m => m.Contains("WorkOrder.View"));
    }

    [Fact]
    public async Task Permission_milik_Administrator_tidak_dapat_diberikan_ke_role_lain()
    {
        var response = await SaveAsync(new SaveRoleRequest(TestHost.UniqueName("Role"), null,
            [Permissions.AccessManagement.User.View, Permissions.AccessManagement.User.Create]));

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(problem.Errors!["permissionKeys"], m => m.Contains("hanya untuk Administrator"));
    }

    [Fact]
    public async Task Permission_tidak_dikenal_ditolak()
    {
        var response = await SaveAsync(new SaveRoleRequest(TestHost.UniqueName("Role"), null, ["Ngawur.Sekali"]));

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(problem.Errors!["permissionKeys"], m => m.Contains("tidak dikenal"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Nama_role_wajib_diisi(string name)
    {
        var problem = ProblemAssert.Is(await SaveAsync(new SaveRoleRequest(name, null, [])),
            HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains("name", problem.Errors!.Keys);
    }

    [Fact]
    public async Task Nama_role_unik_tanpa_peka_huruf_besar_kecil()
    {
        var name = TestHost.UniqueName("Gudang");
        await H.CreateRoleAsync(name);

        var response = await SaveAsync(new SaveRoleRequest(name.ToUpperInvariant(), null, []));

        ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.Duplicate);
    }

    [Fact]
    public async Task Role_tanpa_permission_diizinkan()
    {
        var created = await H.CreateRoleAsync();

        Assert.Empty(created.PermissionKeys);
    }

    [Fact]
    public async Task Ubah_role_mengganti_permission_dan_berlaku_pada_pengguna_tanpa_login_ulang()
    {
        var role = await H.CreateRoleAsync(null, Permissions.CustomerPO.View);
        var user = await H.CreateReadyUserAsync(role.Id);
        var before = (await H.Api.GetAsync("/api/v1/auth/me", user.Token)).Ok().As<MeResponse>();
        Assert.Equal([Permissions.CustomerPO.View], before.Permissions);

        var updated = (await H.Api.PutAsync($"/api/v1/roles/{role.Id}",
            new SaveRoleRequest(role.Name, "Diubah", [Permissions.WorkOrder.View, Permissions.WorkOrder.Create], role.RowVersion), H.AdminToken))
            .Ok().As<RoleDetail>();

        Assert.Equal([Permissions.WorkOrder.Create, Permissions.WorkOrder.View], updated.PermissionKeys);

        // Token yang sama, tanpa login ulang: permission baru langsung berlaku (AC-AM-19).
        var after = (await H.Api.GetAsync("/api/v1/auth/me", user.Token)).Ok().As<MeResponse>();
        Assert.Equal([Permissions.WorkOrder.Create, Permissions.WorkOrder.View], after.Permissions.Order().ToList());
    }

    [Fact]
    public async Task Ubah_role_dengan_rowVersion_usang_memberi_409_walau_hanya_matriks_yang_berbeda()
    {
        var role = await H.CreateRoleAsync(null, Permissions.CustomerPO.View);
        (await H.Api.PutAsync($"/api/v1/roles/{role.Id}",
            new SaveRoleRequest(role.Name, null, [Permissions.CustomerPO.View, Permissions.CustomerPO.Create], role.RowVersion), H.AdminToken)).Ok();

        var stale = await H.Api.PutAsync($"/api/v1/roles/{role.Id}",
            new SaveRoleRequest(role.Name, null, [Permissions.CustomerPO.View, Permissions.CustomerPO.Edit], role.RowVersion), H.AdminToken);

        ProblemAssert.Is(stale, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Role_sistem_tidak_dapat_diubah_atau_dihapus()
    {
        var roles = (await H.Api.GetAsync("/api/v1/roles", H.AdminToken)).Ok().As<List<RoleListItem>>();
        var admin = roles.Single(r => r.IsSystem);
        var detail = (await H.Api.GetAsync($"/api/v1/roles/{admin.Id}", H.AdminToken)).Ok().As<RoleDetail>();

        var update = await H.Api.PutAsync($"/api/v1/roles/{admin.Id}",
            new SaveRoleRequest("Administrator", null, [], detail.RowVersion), H.AdminToken);
        var delete = await H.Api.DeleteAsync($"/api/v1/roles/{admin.Id}", H.AdminToken);

        Assert.Equal(ProblemCodes.SystemRoleImmutable, ProblemAssert.Is(update, HttpStatusCode.Conflict, ProblemTypes.StateConflict).Code);
        Assert.Equal(ProblemCodes.SystemRoleImmutable, ProblemAssert.Is(delete, HttpStatusCode.Conflict, ProblemTypes.StateConflict).Code);
        Assert.Equal(PermissionCatalog.All.Count, detail.PermissionKeys.Count);
    }

    [Fact]
    public async Task Role_yang_dipakai_pengguna_tidak_dapat_dihapus_dan_role_kosong_dapat()
    {
        var used = await H.CreateRoleAsync(null, Permissions.CustomerPO.View);
        await H.CreateUserAsync(used.Id);

        var blocked = await H.Api.DeleteAsync($"/api/v1/roles/{used.Id}", H.AdminToken);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.RoleInUse, problem.Code);
        Assert.Contains("1 pengguna", problem.Detail);

        var unused = await H.CreateRoleAsync(null, Permissions.CustomerPO.View, Permissions.WorkOrder.View);
        var deleted = await H.Api.DeleteAsync($"/api/v1/roles/{unused.Id}", H.AdminToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/roles/{unused.Id}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);

        // Penghapusan role menghapus baris role_permissions (CASCADE).
        await using var db = H.CreateDbContext();
        Assert.Equal(0, await db.RolePermissions.CountAsync(rp => rp.RoleId == unused.Id));
    }

    [Fact]
    public async Task Daftar_role_menampilkan_jumlah_pengguna_dan_permission_serta_katalog_tersedia()
    {
        var role = await H.CreateRoleAsync(null, Permissions.CustomerPO.View, Permissions.WorkOrder.View);
        await H.CreateUserAsync(role.Id);
        await H.CreateUserAsync(role.Id);

        var list = (await H.Api.GetAsync("/api/v1/roles", H.AdminToken)).Ok().As<List<RoleListItem>>();
        var row = list.Single(r => r.Id == role.Id);
        Assert.Equal(2, row.UserCount);
        Assert.Equal(2, row.PermissionCount);
        Assert.True(list[0].IsSystem, "role sistem tampil pertama");

        var catalog = (await H.Api.GetAsync("/api/v1/permissions", H.AdminToken)).Ok().As<List<PermissionItem>>();
        Assert.Equal(PermissionCatalog.All.Count, catalog.Count);
        Assert.Equal(catalog.OrderBy(p => p.SortOrder).Select(p => p.Key), catalog.Select(p => p.Key));
        Assert.Contains(catalog, p => p.Key == Permissions.WorkOrder.Release && p.IsAssignable);
        Assert.Contains(catalog, p => p.Key == Permissions.AccessManagement.Role.Edit && !p.IsAssignable);
    }

    [Fact]
    public async Task Perubahan_role_dicatat_audit_dengan_daftar_permission_ditambah_dan_dilepas()
    {
        var role = await H.CreateRoleAsync(null, Permissions.CustomerPO.View, Permissions.CustomerPO.Create);
        (await H.Api.PutAsync($"/api/v1/roles/{role.Id}",
            new SaveRoleRequest(role.Name, null, [Permissions.CustomerPO.View, Permissions.WorkOrder.View], role.RowVersion), H.AdminToken)).Ok();

        await using var db = H.CreateDbContext();
        var newValue = await db.Database
            .SqlQuery<string>($"SELECT new_value::text AS \"Value\" FROM audit_logs WHERE event_type = 'RoleUpdated' AND target_id = {role.Id.ToString()} ORDER BY id DESC LIMIT 1")
            .SingleAsync();
        Assert.Contains("WorkOrder.View", newValue);
        Assert.Contains("CustomerPO.Create", newValue);
        Assert.Contains("added", newValue);
        Assert.Contains("removed", newValue);
    }
}
