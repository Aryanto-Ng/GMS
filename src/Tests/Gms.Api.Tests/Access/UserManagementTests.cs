using System.Net;
using System.Text.RegularExpressions;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Tests.Access;

[Collection(ApiCollection.Name)]
public sealed class UserManagementTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private async Task<UserDetail> GetUserAsync(Guid id, TestHost? host = null)
    {
        host ??= H;
        return (await host.Api.GetAsync($"/api/v1/users/{id}", host.AdminToken)).Ok().As<UserDetail>();
    }

    // ===== Membuat =====

    [Fact]
    public async Task Buat_pengguna_sukses_aktif_wajib_ganti_password_dan_dapat_login()
    {
        var role = await H.CreateRoleAsync();
        var name = TestHost.UniqueName();

        var response = await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(name, "  Budi Santoso  ", role.Id, TestHost.DefaultPassword), H.AdminToken);

        Assert.Equal(HttpStatusCode.Created, response.Status);
        var created = response.As<UserDetail>();
        Assert.Equal(name, created.UserName);
        Assert.Equal("Budi Santoso", created.FullName);
        Assert.Equal(UserStatus.Active, created.Status);
        Assert.Equal(role.Id, created.RoleId);
        Assert.False(created.HasActiveSession);

        var login = await H.LoginAsync(name, TestHost.DefaultPassword);
        Assert.True(login.MustChangePassword);
    }

    [Fact]
    public async Task Username_unik_tanpa_peka_huruf_besar_kecil()
    {
        var role = await H.CreateRoleAsync();
        var name = TestHost.UniqueName("Budi");
        await H.CreateUserAsync(role.Id, name);

        var response = await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(name.ToUpperInvariant(), "Lain", role.Id, TestHost.DefaultPassword), H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.Duplicate);
        Assert.Contains("userName", problem.Errors!.Keys);
    }

    [Theory]
    [InlineData("ab", "Nama", "Awal12345", "userName")]
    [InlineData("nama pakai spasi", "Nama", "Awal12345", "userName")]
    [InlineData("nama!aneh", "Nama", "Awal12345", "userName")]
    [InlineData("pengguna.ok", "", "Awal12345", "fullName")]
    [InlineData("pengguna.ok", "Nama", "pendek1", "initialPassword")]
    [InlineData("pengguna.ok", "Nama", "TanpaAngka", "initialPassword")]
    [InlineData("pengguna.ok", "Nama", "123456789", "initialPassword")]
    public async Task Buat_pengguna_menolak_isian_tidak_valid(string userName, string fullName, string password, string field)
    {
        var role = await H.CreateRoleAsync();

        var response = await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(userName + (userName == "pengguna.ok" ? Guid.NewGuid().ToString("N")[..6] : ""), fullName, role.Id, password),
            H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(field, problem.Errors!.Keys);
    }

    [Fact]
    public async Task Buat_pengguna_dengan_role_tidak_ada_ditolak()
    {
        var response = await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(TestHost.UniqueName(), "Nama", Guid.NewGuid(), TestHost.DefaultPassword), H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains("roleId", problem.Errors!.Keys);
    }

    // ===== Daftar, detail, ubah =====

    [Fact]
    public async Task Daftar_mendukung_pencarian_filter_role_filter_status_dan_paging()
    {
        var prefix = "lst" + Guid.NewGuid().ToString("N")[..6];
        var roleA = await H.CreateRoleAsync();
        var roleB = await H.CreateRoleAsync();
        var a1 = await H.CreateUserAsync(roleA.Id, prefix + "a1");
        await H.CreateUserAsync(roleA.Id, prefix + "a2");
        await H.CreateUserAsync(roleB.Id, prefix + "b1");
        var detail = await GetUserAsync(a1.Id);
        (await H.Api.PostAsync($"/api/v1/users/{a1.Id}/deactivate?rowVersion={detail.RowVersion}", null, H.AdminToken)).Ok();

        var all = (await H.Api.GetAsync($"/api/v1/users?q={prefix}&pageSize=2", H.AdminToken)).Ok().As<PagedResult<UserListItem>>();
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(2, all.PageSize);

        var page2 = (await H.Api.GetAsync($"/api/v1/users?q={prefix}&pageSize=2&page=2", H.AdminToken)).Ok().As<PagedResult<UserListItem>>();
        Assert.Single(page2.Items);

        var byRole = (await H.Api.GetAsync($"/api/v1/users?q={prefix}&roleId={roleB.Id}", H.AdminToken)).Ok().As<PagedResult<UserListItem>>();
        Assert.Single(byRole.Items);
        Assert.Equal(roleB.Name, byRole.Items[0].RoleName);

        var inactive = (await H.Api.GetAsync($"/api/v1/users?q={prefix}&status=Inactive", H.AdminToken)).Ok().As<PagedResult<UserListItem>>();
        Assert.Equal([prefix + "a1"], inactive.Items.Select(u => u.UserName).ToList());

        var active = (await H.Api.GetAsync($"/api/v1/users?q={prefix}&status=Active", H.AdminToken)).Ok().As<PagedResult<UserListItem>>();
        Assert.Equal(2, active.TotalCount);
    }

    [Fact]
    public async Task Pencarian_aman_terhadap_karakter_khusus_LIKE()
    {
        var response = await H.Api.GetAsync("/api/v1/users?q=%25_%5C", H.AdminToken);

        var result = response.Ok().As<PagedResult<UserListItem>>();
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task Detail_menampilkan_sesi_aktif_dan_pengguna_tidak_ada_memberi_404()
    {
        var user = await H.CreateReadyUserAsync(H.AdminRoleId);

        var detail = await GetUserAsync(user.Id);
        Assert.True(detail.HasActiveSession);
        Assert.NotNull(detail.ActiveSessionSince);

        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/users/{Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Ubah_pengguna_mengganti_nama_dan_role_tetapi_username_tidak_berubah()
    {
        var roleA = await H.CreateRoleAsync();
        var roleB = await H.CreateRoleAsync();
        var created = await H.CreateUserAsync(roleA.Id);

        var response = await H.Api.PutAsync($"/api/v1/users/{created.Id}",
            new UpdateUserRequest("Nama Baru", roleB.Id, created.RowVersion), H.AdminToken);

        var updated = response.Ok().As<UserDetail>();
        Assert.Equal("Nama Baru", updated.FullName);
        Assert.Equal(roleB.Id, updated.RoleId);
        Assert.Equal(created.UserName, updated.UserName);
        Assert.NotEqual(created.RowVersion, updated.RowVersion);
    }

    [Fact]
    public async Task Ubah_pengguna_dengan_rowVersion_usang_memberi_409_konkurensi()
    {
        var role = await H.CreateRoleAsync();
        var created = await H.CreateUserAsync(role.Id);
        (await H.Api.PutAsync($"/api/v1/users/{created.Id}", new UpdateUserRequest("Pertama", role.Id, created.RowVersion), H.AdminToken)).Ok();

        var stale = await H.Api.PutAsync($"/api/v1/users/{created.Id}",
            new UpdateUserRequest("Kedua", role.Id, created.RowVersion), H.AdminToken);

        ProblemAssert.Is(stale, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
    }

    // ===== Reset password =====

    [Fact]
    public async Task Reset_password_membuat_password_sementara_mencabut_sesi_dan_wajib_ganti()
    {
        var user = await H.CreateReadyUserAsync(H.AdminRoleId);

        var response = await H.Api.PostAsync($"/api/v1/users/{user.Id}/reset-password", new ResetPasswordRequest(null), H.AdminToken);

        var temp = response.Ok().As<ResetPasswordResponse>().TemporaryPassword;
        Assert.Matches(new Regex("^(?=.*[A-Za-z])(?=.*[0-9]).{12}$"), temp);

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/auth/me", user.Token), HttpStatusCode.Unauthorized, ProblemTypes.SessionExpired);
        ProblemAssert.Is(await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, user.Password)),
            HttpStatusCode.Unauthorized, ProblemTypes.InvalidCredentials);

        var login = await H.LoginAsync(user.UserName, temp);
        Assert.True(login.MustChangePassword);
    }

    [Fact]
    public async Task Reset_password_dengan_nilai_sendiri_dan_membuka_kunci_akun()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        for (var i = 0; i < 5; i++)
        {
            await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, "Salah12345"));
        }

        var response = await H.Api.PostAsync($"/api/v1/users/{user.Id}/reset-password", new ResetPasswordRequest("Pilihan12345"), H.AdminToken);

        Assert.Equal("Pilihan12345", response.Ok().As<ResetPasswordResponse>().TemporaryPassword);
        Assert.False(string.IsNullOrEmpty((await H.LoginAsync(user.UserName, "Pilihan12345")).Token));
    }

    [Fact]
    public async Task Reset_password_menolak_password_yang_tidak_memenuhi_aturan()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);

        var response = await H.Api.PostAsync($"/api/v1/users/{user.Id}/reset-password", new ResetPasswordRequest("abc"), H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains("newPassword", problem.Errors!.Keys);
    }

    // ===== Nonaktif / aktif =====

    [Fact]
    public async Task Nonaktifkan_menolak_login_mencabut_sesi_dan_dapat_diaktifkan_kembali()
    {
        var role = await H.CreateRoleAsync();
        var user = await H.CreateReadyUserAsync(role.Id);
        var detail = await GetUserAsync(user.Id);

        var deactivated = (await H.Api.PostAsync($"/api/v1/users/{user.Id}/deactivate?rowVersion={detail.RowVersion}", null, H.AdminToken))
            .Ok().As<UserDetail>();
        Assert.Equal(UserStatus.Inactive, deactivated.Status);

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/auth/me", user.Token), HttpStatusCode.Unauthorized, ProblemTypes.AccountDisabled);
        ProblemAssert.Is(await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, user.Password)),
            HttpStatusCode.Unauthorized, ProblemTypes.InvalidCredentials);

        var activated = (await H.Api.PostAsync($"/api/v1/users/{user.Id}/activate?rowVersion={deactivated.RowVersion}", null, H.AdminToken))
            .Ok().As<UserDetail>();
        Assert.Equal(UserStatus.Active, activated.Status);
        Assert.False(string.IsNullOrEmpty((await H.LoginAsync(user.UserName, user.Password)).Token));
    }

    [Fact]
    public async Task Nonaktifkan_dengan_rowVersion_usang_memberi_409()
    {
        var role = await H.CreateRoleAsync();
        var user = await H.CreateUserAsync(role.Id);

        var response = await H.Api.PostAsync($"/api/v1/users/{user.Id}/deactivate?rowVersion={user.RowVersion + 7}", null, H.AdminToken);

        ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Administrator_tidak_dapat_menonaktifkan_akunnya_sendiri()
    {
        var admin = await H.CreateReadyAdminAsync();
        var detail = await GetUserAsync(admin.Id);

        var response = await H.Api.PostAsync($"/api/v1/users/{admin.Id}/deactivate?rowVersion={detail.RowVersion}", null, admin.Token);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.CannotDeactivateSelf, problem.Code);
    }

    [Fact]
    public async Task Administrator_aktif_terakhir_tidak_dapat_diturunkan_role_nya()
    {
        var iso = await fixture.CreateIsolatedAsync();
        var me = (await iso.Api.GetAsync("/api/v1/auth/me", iso.AdminToken)).Ok().As<MeResponse>();
        var custom = await iso.CreateRoleAsync();
        var detail = await GetUserAsync(me.User.Id, iso);
        Assert.True(detail.IsLastActiveAdministrator);

        var response = await iso.Api.PutAsync($"/api/v1/users/{me.User.Id}",
            new UpdateUserRequest("Admin", custom.Id, detail.RowVersion), iso.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.LastAdministrator, problem.Code);

        // Setelah ada Administrator kedua, perubahan diizinkan.
        await iso.CreateUserAsync(iso.AdminRoleId);
        var fresh = await GetUserAsync(me.User.Id, iso);
        Assert.False(fresh.IsLastActiveAdministrator);
        (await iso.Api.PutAsync($"/api/v1/users/{me.User.Id}",
            new UpdateUserRequest("Admin", custom.Id, fresh.RowVersion), iso.AdminToken)).Ok();
    }

    // ===== Akhiri sesi & buka kunci =====

    [Fact]
    public async Task Akhiri_sesi_memaksa_pengguna_keluar_dan_idempoten()
    {
        var role = await H.CreateRoleAsync();
        var user = await H.CreateReadyUserAsync(role.Id);
        Assert.True((await GetUserAsync(user.Id)).HasActiveSession);

        var first = await H.Api.PostAsync($"/api/v1/users/{user.Id}/end-session", null, H.AdminToken);
        Assert.Equal(HttpStatusCode.NoContent, first.Status);

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/auth/me", user.Token), HttpStatusCode.Unauthorized, ProblemTypes.AccountDisabled);
        Assert.False((await GetUserAsync(user.Id)).HasActiveSession);

        var again = await H.Api.PostAsync($"/api/v1/users/{user.Id}/end-session", null, H.AdminToken);
        Assert.Equal(HttpStatusCode.NoContent, again.Status);
    }

    // ===== Penegakan akses di server =====

    [Fact]
    public async Task Pengguna_dengan_semua_permission_yang_dapat_diberikan_tetap_tidak_bisa_mengelola_akses()
    {
        var everything = PermissionCatalog.All.Where(p => p.IsAssignable).Select(p => p.Key).ToArray();
        var role = await H.CreateRoleAsync(null, everything);
        var user = await H.CreateReadyUserAsync(role.Id);

        foreach (var path in new[] { "/api/v1/users", "/api/v1/roles", "/api/v1/permissions", "/api/v1/audit-logs?from=2026-01-01&to=2026-01-02" })
        {
            ProblemAssert.Is(await H.Api.GetAsync(path, user.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        }

        ProblemAssert.Is(
            await H.Api.PostAsync("/api/v1/users", new CreateUserRequest(TestHost.UniqueName(), "X", role.Id, TestHost.DefaultPassword), user.Token),
            HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    [Fact]
    public async Task Tanpa_login_semua_endpoint_pengelolaan_memberi_401()
    {
        foreach (var path in new[] { "/api/v1/users", "/api/v1/roles", "/api/v1/permissions", "/api/v1/audit-logs" })
        {
            ProblemAssert.Is(await H.Api.GetAsync(path), HttpStatusCode.Unauthorized, ProblemTypes.SessionExpired);
        }
    }

    [Fact]
    public async Task Pembuatan_dan_pengubahan_pengguna_dicatat_pada_audit_log_tanpa_password()
    {
        var role = await H.CreateRoleAsync();
        var name = TestHost.UniqueName("aud");
        var created = (await H.Api.PostAsync("/api/v1/users",
            new CreateUserRequest(name, "Audit Uji", role.Id, "RahasiaAwal12345"), H.AdminToken)).Ok().As<UserDetail>();
        await H.Api.PostAsync($"/api/v1/users/{created.Id}/reset-password", new ResetPasswordRequest("RahasiaReset12345"), H.AdminToken);

        await using var db = H.CreateDbContext();
        var events = await db.AuditLogs.Where(a => a.TargetId == created.Id.ToString()).Select(a => a.EventType).ToListAsync();
        Assert.Contains("UserCreated", events);
        Assert.Contains("PasswordReset", events);

        var leaked = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM audit_logs WHERE coalesce(old_value::text, '') || coalesce(new_value::text, '') ILIKE '%Rahasia%'")
            .SingleAsync();
        Assert.Equal(0, leaked);
    }
}
