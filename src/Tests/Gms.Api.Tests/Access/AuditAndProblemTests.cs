using System.Net;
using Gms.Api.Common;
using Gms.Api.Tests.Infrastructure;
using Gms.Domain.Access;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gms.Api.Tests.Access;

[Collection(ApiCollection.Name)]
public sealed class AuditAndProblemTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private static string Today(int offsetDays = 0) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offsetDays).ToString("yyyy-MM-dd");

    private string Range => $"from={Today(-1)}&to={Today(1)}";

    // ===== Audit log =====

    [Fact]
    public async Task Login_berhasil_dan_gagal_tercatat_dengan_pelaku_dan_jenis_peristiwa()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest(user.UserName, "Salah12345"));
        await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        var failed = (await H.Api.GetAsync($"/api/v1/audit-logs?{Range}&eventType=LoginFailed&q={user.UserName}", H.AdminToken))
            .Ok().As<PagedResult<AuditLogItem>>();
        Assert.Single(failed.Items);
        Assert.Equal(user.UserName, failed.Items[0].ActorUsername);
        Assert.Equal(user.Id, failed.Items[0].ActorUserId);
        Assert.Contains("WrongPassword", failed.Items[0].NewValueJson);

        var success = (await H.Api.GetAsync($"/api/v1/audit-logs?{Range}&eventType=LoginSucceeded&actorId={user.Id}", H.AdminToken))
            .Ok().As<PagedResult<AuditLogItem>>();
        Assert.Single(success.Items);
    }

    [Fact]
    public async Task Daftar_audit_terurut_terbaru_dulu_dan_mendukung_paging()
    {
        var admin = await H.CreateReadyAdminAsync();

        var page = (await H.Api.GetAsync($"/api/v1/audit-logs?{Range}&actorId={admin.Id}&pageSize=2", H.AdminToken))
            .Ok().As<PagedResult<AuditLogItem>>();

        Assert.True(page.TotalCount >= 2);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.Items[0].OccurredAt >= page.Items[1].OccurredAt);
    }

    [Theory]
    [InlineData("", "from")]
    [InlineData("from=2026-06-10&to=2026-06-01", "to")]
    [InlineData("from=2024-01-01&to=2026-11-01", "to")]
    public async Task Rentang_tanggal_audit_wajib_dan_dibatasi(string query, string field)
    {
        var response = await H.Api.GetAsync($"/api/v1/audit-logs?{query}", H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains(field, problem.Errors!.Keys);
    }

    [Fact]
    public async Task Pengguna_tanpa_permission_audit_tidak_dapat_melihat_log()
    {
        var role = await H.CreateRoleAsync();
        var user = await H.CreateReadyUserAsync(role.Id);

        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/audit-logs?{Range}", user.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    // ===== Bentuk Problem Details =====

    [Fact]
    public async Task Rute_tidak_dikenal_memberi_404_Problem_Details_dengan_traceId()
    {
        var response = await H.Api.GetAsync("/api/v1/tidak-ada-rute-ini");

        ProblemAssert.Is(response, HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Isi_JSON_rusak_memberi_400_bad_request_tanpa_membocorkan_detail()
    {
        var response = await H.Api.PostAsync("/api/v1/auth/login", "{ ini bukan json");

        var problem = ProblemAssert.Is(response, HttpStatusCode.BadRequest, ProblemTypes.BadRequest);
        Assert.DoesNotContain("JsonException", response.Body);
        Assert.DoesNotContain("   at ", response.Body);
        Assert.Equal("Permintaan tidak valid.", problem.Title);
    }

    [Fact]
    public async Task Galat_validasi_memuat_errors_per_field_dalam_camelCase()
    {
        var response = await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest("", ""));

        Assert.Contains("\"errors\"", response.Body);
        Assert.Contains("\"username\"", response.Body);
        Assert.Contains("\"traceId\"", response.Body);
        Assert.DoesNotContain("\"Errors\"", response.Body);
    }

    [Fact]
    public void Pengecualian_tak_terduga_menjadi_500_generik_tanpa_membocorkan_pesan_internal()
    {
        var problem = ProblemWriter.Map(new InvalidOperationException("kata-sandi-db=rahasia"), NullLogger.Instance);

        Assert.Equal(ProblemTypes.InternalError, problem.Type);
        Assert.Equal(500, problem.Status);
        Assert.DoesNotContain("rahasia", problem.Detail);
        Assert.DoesNotContain("rahasia", problem.Title);
    }

    [Fact]
    public async Task Respons_401_membawa_pesan_Bahasa_Indonesia_yang_sesuai_jenis_sesi()
    {
        var user = await H.CreateUserAsync(H.AdminRoleId);
        var first = await H.LoginAsync(user.UserName, TestHost.DefaultPassword);
        await H.LoginAsync(user.UserName, TestHost.DefaultPassword);

        var response = await H.Api.GetAsync("/api/v1/auth/me", first.Token);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Unauthorized, ProblemTypes.SessionReplaced);
        Assert.Contains("perangkat lain", problem.Detail);
    }

    // ===== Constraint database terkait Access =====

    [Fact]
    public async Task Database_menolak_sesi_aktif_kedua_untuk_pengguna_yang_sama()
    {
        var user = await H.CreateReadyUserAsync(H.AdminRoleId);
        await using var db = H.CreateDbContext();
        db.UserSessions.Add(new UserSession
        {
            UserId = user.Id, TokenHash = [1, 2, 3, (byte)Random.Shared.Next(255)],
            CreatedAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var pg = ProblemAssert.Postgres(ex);
        Assert.Equal("23505", pg.SqlState);
        Assert.Equal("ux_user_sessions_active_per_user", pg.ConstraintName);
    }

    [Theory]
    [InlineData("UPDATE audit_logs SET event_type = 'Diubah'")]
    [InlineData("DELETE FROM audit_logs")]
    [InlineData("TRUNCATE audit_logs")]
    public async Task Database_menolak_perubahan_penghapusan_dan_truncate_audit_log(string sql)
    {
        await H.Api.PostAsync("/api/v1/auth/login", new LoginRequest("tidak.ada.audit", "x"));
        await using var db = H.CreateDbContext();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Equal("23000", ProblemAssert.Postgres(ex).SqlState);
        Assert.True(await db.AuditLogs.AnyAsync(), "baris audit harus tetap ada");
    }
}
