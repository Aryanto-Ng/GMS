using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Tests.Access;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    // ===== Login =====

    [Fact]
    public async Task Login_sukses_mengembalikan_token_profil_dan_seluruh_permission_Administrator()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);

        var login = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        Assert.True(login.Token.Length >= 40, "token harus acak dan panjang (>= 256 bit)");
        Assert.Equal(30, login.ExpiresInMinutes);
        Assert.True(login.MustChangePassword);
        Assert.True(login.User.IsAdministrator);
        Assert.Equal(user.UserName, login.User.UserName);
        Assert.Equal(PermissionCatalog.All.Count, login.Permissions.Count);
    }

    [Fact]
    public async Task Login_password_salah_memberi_pesan_generik()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);

        var response = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, "SalahBanget1"));

        var problem = ProblemAssert.Is(response, HttpStatusCode.Unauthorized, ProblemTypes.InvalidCredentials);
        Assert.Equal("Username atau password salah.", problem.Detail);
    }

    [Fact]
    public async Task Login_username_tidak_dikenal_memberi_pesan_yang_sama_persis()
    {
        var response = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest("tidak.ada." + Guid.NewGuid().ToString("N")[..6], "Apa12345"));

        var problem = ProblemAssert.Is(response, HttpStatusCode.Unauthorized, ProblemTypes.InvalidCredentials);
        Assert.Equal("Username atau password salah.", problem.Detail);
    }

    [Fact]
    public async Task Login_pengguna_nonaktif_ditolak_dengan_pesan_generik_walau_password_benar()
    {
        var role = await H.CreateRoleAsync();
        var user = await H.CreateUserAsync(role.Id);
        var detail = (await H.Api.GetAsync($"/api/v1/users/{user.Id}", H.AdminToken)).Ok().As<UserDetail>();
        (await H.Api.PostAsync($"/api/v1/users/{user.Id}/deactivate?rowVersion={detail.RowVersion}", null, H.AdminToken)).Ok();

        var response = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, TestHost.DefaultPassword));

        var problem = ProblemAssert.Is(response, HttpStatusCode.Unauthorized, ProblemTypes.InvalidCredentials);
        Assert.Equal("Username atau password salah.", problem.Detail);
    }

    [Theory]
    [InlineData("", "Apa12345", "username")]
    [InlineData("   ", "Apa12345", "username")]
    [InlineData("admin", "", "password")]
    public async Task Login_isian_kosong_memberi_galat_validasi(string username, string password, string field)
    {
        var response = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(username, password));

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.NotNull(problem.Errors);
        Assert.Contains(field, problem.Errors!.Keys);
    }

    [Fact]
    public async Task Lima_kali_gagal_mengunci_akun_dan_Administrator_dapat_membukanya()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrong = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, "Salah12345"));
            ProblemAssert.Is(wrong, HttpStatusCode.Unauthorized, ProblemTypes.InvalidCredentials);
        }

        var fifth = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, "Salah12345"));
        var locked = ProblemAssert.Is(fifth, HttpStatusCode.Unauthorized, ProblemTypes.AccountLocked);
        Assert.NotNull(locked.LockedUntil);
        Assert.True(locked.LockedUntil > DateTimeOffset.UtcNow.AddMinutes(14), "kunci seharusnya sekitar 15 menit");

        // Password benar pun ditolak selama terkunci.
        var correctWhileLocked = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, TestHost.DefaultPassword));
        ProblemAssert.Is(correctWhileLocked, HttpStatusCode.Unauthorized, ProblemTypes.AccountLocked);

        var detail = (await H.Api.GetAsync($"/api/v1/users/{user.Id}", H.AdminToken)).Ok().As<UserDetail>();
        Assert.Equal(UserStatus.Locked, detail.Status);

        (await H.Api.PostAsync($"/api/v1/users/{user.Id}/unlock", null, H.AdminToken)).Ok();

        var afterUnlock = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);
        Assert.False(string.IsNullOrEmpty(afterUnlock.Token));
    }

    [Fact]
    public async Task Login_berhasil_mereset_hitungan_gagal()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, "Salah12345"));
        }

        await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        var detail = (await H.Api.GetAsync($"/api/v1/users/{user.Id}", H.AdminToken)).Ok().As<UserDetail>();
        Assert.Equal(0, detail.FailedLoginCount);
    }

    // ===== Sesi =====

    [Fact]
    public async Task Login_dari_perangkat_lain_menggantikan_sesi_lama()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        var first = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);
        var second = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        var oldSession = await H.Api.GetAsync("/api/v1/auth/me", first.Token);
        ProblemAssert.Is(oldSession, HttpStatusCode.Unauthorized, ProblemTypes.SessionReplaced);

        var newSession = await H.Api.GetAsync("/api/v1/auth/me", second.Token);
        Assert.True(newSession.IsSuccess, newSession.Body);

        await using var db = H.CreateDbContext();
        Assert.Equal(1, await db.UserSessions.CountAsync(s => s.UserId == user.Id && s.RevokedAt == null));
        Assert.Equal(SessionRevokedReason.Replaced,
            await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt != null).Select(s => s.RevokedReason).SingleAsync());
    }

    [Fact]
    public async Task Sesi_berakhir_setelah_30_menit_tidak_aktif()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        var login = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        await using (var db = H.CreateDbContext())
        {
            await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow.AddMinutes(-31)));
        }

        var response = await H.Api.GetAsync("/api/v1/auth/me", login.Token);
        ProblemAssert.Is(response, HttpStatusCode.Unauthorized, ProblemTypes.SessionExpired);

        await using var check = H.CreateDbContext();
        Assert.Equal(SessionRevokedReason.Timeout,
            await check.UserSessions.Where(s => s.UserId == user.Id).Select(s => s.RevokedReason).SingleAsync());

        // Login ulang tetap dapat dilakukan.
        Assert.False(string.IsNullOrEmpty((await H.LoginAsync(user.UserName, TestHost.DefaultPassword)).Token));
    }

    [Fact]
    public async Task Sesi_yang_masih_dalam_batas_tetap_valid_dan_aktivitas_diperbarui_dengan_throttle()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        var login = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        await using (var db = H.CreateDbContext())
        {
            await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, DateTimeOffset.UtcNow.AddMinutes(-20)));
        }

        (await H.Api.GetAsync("/api/v1/auth/me", login.Token)).Ok();

        await using var check = H.CreateDbContext();
        var last = await check.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).Select(s => s.LastActivityAt).SingleAsync();
        Assert.True(last > DateTimeOffset.UtcNow.AddMinutes(-1), "aktivitas terakhir harus diperpanjang ke sekarang");
    }

    [Fact]
    public async Task Logout_mencabut_sesi()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        var login = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        var logout = await H.Api.PostAsync("/api/v1/auth/logout", null, login.Token);
        Assert.Equal(HttpStatusCode.NoContent, logout.Status);

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/auth/me", login.Token), HttpStatusCode.Unauthorized, ProblemTypes.SessionExpired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("token-ngawur")]
    public async Task Tanpa_atau_dengan_token_tidak_valid_ditolak_401(string? token)
    {
        var response = await H.Api.GetAsync("/api/v1/users", token);

        ProblemAssert.Is(response, HttpStatusCode.Unauthorized, ProblemTypes.SessionExpired);
    }

    [Fact]
    public async Task Me_mengembalikan_profil_dan_permission_sesuai_role()
    {
        var role = await H.CreateRoleAsync(null, Permissions.CustomerPO.View, Permissions.WorkOrder.View);
        var user = await H.CreateReadyUserAsync(role.Id);

        var me = (await H.Api.GetAsync("/api/v1/auth/me", user.Token)).Ok().As<MeResponse>();

        Assert.Equal(user.UserName, me.User.UserName);
        Assert.False(me.User.IsAdministrator);
        Assert.False(me.MustChangePassword);
        Assert.Equal([Permissions.CustomerPO.View, Permissions.WorkOrder.View], me.Permissions.Order().ToList());
    }

    // ===== Wajib ganti password & ganti password =====

    [Fact]
    public async Task Akun_baru_wajib_ganti_password_sebelum_memakai_endpoint_lain()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        var login = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/users", login.Token), HttpStatusCode.Forbidden, ProblemTypes.MustChangePassword);
        (await H.Api.GetAsync("/api/v1/auth/me", login.Token)).Ok("/auth/* tetap diizinkan");

        (await H.Api.PostAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(TestHost.DefaultPassword, "Baru123456"), login.Token)).Ok();

        (await H.Api.GetAsync("/api/v1/users", login.Token)).Ok("setelah ganti password, token yang sama berlaku");
    }

    [Fact]
    public async Task Ganti_password_sukses_password_lama_tidak_berlaku_lagi()
    {
        var user = await H.CreateReadyUserAsync(H.AdminRoleId);

        (await H.Api.PostAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(user.Password, "LebihBaru789"), user.Token)).Ok();

        ProblemAssert.Is(await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, user.Password)),
            HttpStatusCode.Unauthorized, ProblemTypes.InvalidCredentials);
        Assert.False(string.IsNullOrEmpty((await H.LoginAsync(user.UserName, "LebihBaru789")).Token));
    }

    [Theory]
    [InlineData("SalahLama1", "BaruAman123", "currentPassword", "salah")]
    [InlineData("PASSWORD_LAMA", "Final12345", "newPassword", "sama")]
    [InlineData("PASSWORD_LAMA", "Pendek1", "newPassword", "8 karakter")]
    [InlineData("PASSWORD_LAMA", "TanpaAngkaSaja", "newPassword", "angka")]
    [InlineData("PASSWORD_LAMA", "12345678901", "newPassword", "huruf")]
    public async Task Ganti_password_menolak_isian_tidak_valid(string current, string newPassword, string field, string messagePart)
    {
        var user = await H.CreateReadyUserAsync(H.AdminRoleId);
        var currentPassword = current == "PASSWORD_LAMA" ? user.Password : current;

        var response = await H.Api.PostAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(currentPassword, newPassword), user.Token);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(field, problem.Errors!.Keys);
        Assert.Contains(problem.Errors[field], m => m.Contains(messagePart, StringComparison.OrdinalIgnoreCase));
    }
}
