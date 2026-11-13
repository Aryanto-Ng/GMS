using System.Net;
using Gms.Api.Features.Notifications;
using Gms.Api.Tests.Infrastructure;
using Gms.Domain.Notifications;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.Notifications;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Notifications;

/// <summary>Notifikasi (FSD Notifikasi v1.0): katalog, penerbit, penyajian per izin, pengaturan, dan pemeriksaan terjadwal.</summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationTests(ApiFixture fixture)
{
    private static readonly string[] ForbiddenParamFragments = ["rp", "rupiah", "tarif", "rate", "price", "harga", "upah", "amount", "total", "gaji"];

    // ===== Katalog =====

    [Fact]
    public void Katalog_memuat_16_jenis_unik_dengan_izin_yang_terdaftar_dan_parameter_tanpa_data_uang()
    {
        Assert.Equal(16, NotificationCatalog.All.Count);
        Assert.Equal(12, NotificationCatalog.All.Count(d => d.Kind == NotificationKind.Event));
        Assert.Equal(4, NotificationCatalog.All.Count(d => d.Kind == NotificationKind.Condition));
        Assert.Equal(NotificationCatalog.All.Count, NotificationCatalog.All.Select(d => d.TypeCode).Distinct().Count());

        var keys = PermissionCatalog.All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var def in NotificationCatalog.All)
        {
            Assert.NotEmpty(def.RequiredPermissions);
            Assert.All(def.RequiredPermissions, p => Assert.Contains(p, keys));
            Assert.NotEmpty(def.ParamKeys);
            foreach (var key in def.ParamKeys)
            {
                Assert.DoesNotContain(ForbiddenParamFragments, f => key.Contains(f, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Penerbit_hanya_meloloskan_kunci_parameter_katalog_dan_memangkas_nilai_panjang()
    {
        var def = NotificationCatalog.Find("NE-08")!;
        var json = NotificationPublisher.BuildParams(def, new Dictionary<string, string>
        {
            ["code"] = new string('x', 500), ["releasedQty"] = "40", ["tarif"] = "2500", ["rupiah"] = "99",
        });
        var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
        Assert.Equal(["code", "releasedQty"], parsed.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(200, parsed["code"].Length);
    }

    // ===== Seeder dan pengaturan =====

    [Fact]
    public async Task Seeder_membuat_pengaturan_bawaan_dan_16_jenis_aktif_secara_idempoten()
    {
        var h = await fixture.CreateIsolatedAsync();
        var settings = (await h.Api.GetAsync("/api/v1/notifications/settings", h.AdminToken)).Ok().As<NotificationSettingsDto>();
        Assert.Equal(3, settings.DueSoonDays);
        Assert.Equal("06:00", settings.ScheduleTime);
        Assert.Equal(90, settings.RetentionDays);
        Assert.Equal(16, settings.Types.Count);
        Assert.All(settings.Types, t => Assert.True(t.Enabled));

        using var scope = h.Services.CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<Gms.Api.Hosting.IDataSeeder>().Where(s => s is NotificationSeeder))
        {
            await seeder.SeedAsync(CancellationToken.None);
            await seeder.SeedAsync(CancellationToken.None);
        }

        await using var db = h.CreateDbContext();
        Assert.Equal(16, await db.NotificationTypeSettings.CountAsync());
        Assert.Equal(1, await db.NotificationOptionsRows.CountAsync());
    }

    [Fact]
    public async Task Pengaturan_dapat_diubah_dengan_validasi_konkurensi_izin_dan_audit()
    {
        var h = await fixture.CreateIsolatedAsync();
        var current = (await h.Api.GetAsync("/api/v1/notifications/settings", h.AdminToken)).Ok().As<NotificationSettingsDto>();

        UpdateNotificationSettingsRequest Req(int due = 5, string time = "07:30", int keep = 120, bool ne08 = false, uint? version = null) => new(
            due, time, keep, current.Types.Select(t => t with { Enabled = t.TypeCode == "NE-08" ? ne08 : t.Enabled }).ToList(), version ?? current.RowVersion);

        // Validasi: ambang, jam, retensi, jenis tidak dikenal.
        var bad = await h.Api.PutAsync("/api/v1/notifications/settings",
            new UpdateNotificationSettingsRequest(0, "25:61", 5, [new NotificationTypeSettingDto("NX-99", true)], current.RowVersion), h.AdminToken);
        var problem = ProblemAssert.Is(bad, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains("dueSoonDays", bad.Body);
        Assert.Contains("scheduleTime", bad.Body);
        Assert.Contains("retentionDays", bad.Body);
        Assert.Contains("types[0].typeCode", bad.Body);
        Assert.NotNull(problem);

        var ok = (await h.Api.PutAsync("/api/v1/notifications/settings", Req(), h.AdminToken)).Ok().As<NotificationSettingsDto>();
        Assert.Equal((5, "07:30", 120), (ok.DueSoonDays, ok.ScheduleTime, ok.RetentionDays));
        Assert.False(ok.Types.Single(t => t.TypeCode == "NE-08").Enabled);
        Assert.NotEqual(current.RowVersion, ok.RowVersion);
        Assert.NotNull(ok.UpdatedAt);

        // Versi usang = 409.
        ProblemAssert.Is(await h.Api.PutAsync("/api/v1/notifications/settings", Req(due: 6, version: current.RowVersion), h.AdminToken),
            HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);

        // Hanya mengubah pengaktifan jenis tetap menaikkan versi.
        var again = (await h.Api.PutAsync("/api/v1/notifications/settings", Req(ne08: true, version: ok.RowVersion), h.AdminToken)).Ok().As<NotificationSettingsDto>();
        Assert.NotEqual(ok.RowVersion, again.RowVersion);

        // Izin: View boleh membaca tetapi tidak mengubah; tanpa izin 403.
        var viewer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.Notifikasi.Pengaturan.View)).Id);
        Assert.True((await h.Api.GetAsync("/api/v1/notifications/settings", viewer.Token)).IsSuccess);
        ProblemAssert.Is(await h.Api.PutAsync("/api/v1/notifications/settings", Req(version: again.RowVersion), viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        var nobody = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        ProblemAssert.Is(await h.Api.GetAsync("/api/v1/notifications/settings", nobody.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);

        // Audit mencatat perubahan.
        await using var db = h.CreateDbContext();
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EventType == "NotificationSettingsChanged"));
    }

    // ===== Penerbit dan penyajian =====

    private static NotificationEvent Ne08(Guid? actor, string key = "k1") => new(
        "NE-08", key, new Dictionary<string, string> { ["code"] = "PO-202611-0001", ["releasedQty"] = "40" },
        new NotificationTarget(NotificationTargetKind.PurchaseOrder, Guid.NewGuid().ToString()), actor);

    private static async Task PublishAsync(TestHost h, NotificationEvent evt)
    {
        using var scope = h.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationPublisher>().PublishAsync(evt);
        await scope.ServiceProvider.GetRequiredService<Gms.Infrastructure.Persistence.GmsDbContext>().SaveChangesAsync();
    }

    [Fact]
    public async Task Penerima_adalah_pengguna_aktif_berizin_bukan_pelaku_dan_tidak_ada_notifikasi_untuk_yang_lain()
    {
        var h = await fixture.CreateIsolatedAsync();
        var poRole = await h.CreateRoleAsync(null, Permissions.CustomerPO.View);
        var other = await h.CreateRoleAsync(null, Permissions.Pengiriman.View);
        var actor = await h.CreateReadyUserAsync(poRole.Id);
        var peer = await h.CreateReadyUserAsync(poRole.Id);
        var inactive = await h.CreateReadyUserAsync(poRole.Id);
        var unrelated = await h.CreateReadyUserAsync(other.Id);
        await using (var db = h.CreateDbContext())
        {
            await db.Users.Where(u => u.Id == inactive.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        }

        await PublishAsync(h, Ne08(actor.Id));

        await using var check = h.CreateDbContext();
        var recipients = await check.Notifications.Where(n => n.TypeCode == "NE-08").Select(n => n.RecipientUserId).ToListAsync();
        Assert.Contains(peer.Id, recipients);
        Assert.DoesNotContain(actor.Id, recipients);
        Assert.DoesNotContain(inactive.Id, recipients);
        Assert.DoesNotContain(unrelated.Id, recipients);
        Assert.Equal(recipients.Count, recipients.Distinct().Count());

        Assert.Equal(1, (await h.Api.GetAsync("/api/v1/notifications/summary", peer.Token)).Ok().As<NotificationSummary>().UnreadCount);
        Assert.Equal(0, (await h.Api.GetAsync("/api/v1/notifications/summary", unrelated.Token)).Ok().As<NotificationSummary>().UnreadCount);
        Assert.Equal(0, (await h.Api.GetAsync("/api/v1/notifications/summary", actor.Token)).Ok().As<NotificationSummary>().UnreadCount);
    }

    [Fact]
    public async Task Jenis_yang_dinonaktifkan_tidak_membuat_notifikasi()
    {
        var h = await fixture.CreateIsolatedAsync();
        var peer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        var current = (await h.Api.GetAsync("/api/v1/notifications/settings", h.AdminToken)).Ok().As<NotificationSettingsDto>();
        (await h.Api.PutAsync("/api/v1/notifications/settings", new UpdateNotificationSettingsRequest(
            current.DueSoonDays, current.ScheduleTime, current.RetentionDays,
            current.Types.Select(t => t with { Enabled = t.TypeCode != "NE-08" }).ToList(), current.RowVersion), h.AdminToken)).Ok();

        await PublishAsync(h, Ne08(null));
        Assert.Equal(0, (await h.Api.GetAsync("/api/v1/notifications/summary", peer.Token)).Ok().As<NotificationSummary>().UnreadCount);
    }

    [Fact]
    public async Task Daftar_hitung_tandai_terbaca_dan_pencarian_hanya_menyentuh_milik_sendiri()
    {
        var h = await fixture.CreateIsolatedAsync();
        var poRole = await h.CreateRoleAsync(null, Permissions.CustomerPO.View);
        var a = await h.CreateReadyUserAsync(poRole.Id);
        var b = await h.CreateReadyUserAsync(poRole.Id);
        for (var i = 0; i < 3; i++)
        {
            await PublishAsync(h, Ne08(null, $"k{i}"));
        }

        var list = (await h.Api.GetAsync("/api/v1/notifications?pageSize=2", a.Token)).Ok().As<PagedResult<NotificationItem>>();
        Assert.Equal(3, list.TotalCount);
        Assert.Equal(2, list.Items.Count);
        Assert.True(list.Items[0].CreatedAt >= list.Items[1].CreatedAt);
        Assert.Equal("PO-202611-0001", list.Items[0].Params["code"]);
        Assert.Equal(NotificationTargetKind.PurchaseOrder, list.Items[0].Target!.Kind);
        Assert.Equal(NotificationCategory.Penjualan, list.Items[0].Category);

        var counts = (await h.Api.GetAsync("/api/v1/notifications/counts", a.Token)).Ok().As<NotificationCounts>();
        Assert.Equal((3, 3, 3), (counts.All, counts.Unread, counts.ByCategory[NotificationCategory.Penjualan]));
        Assert.Equal(0, counts.ByCategory[NotificationCategory.Gudang]);

        // Pencarian pada parameter; karakter pola di-escape.
        Assert.Equal(3, (await h.Api.GetAsync("/api/v1/notifications?q=0001", a.Token)).Ok().As<PagedResult<NotificationItem>>().TotalCount);
        Assert.Equal(0, (await h.Api.GetAsync("/api/v1/notifications?q=%25", a.Token)).Ok().As<PagedResult<NotificationItem>>().TotalCount);

        // Menandai terbaca: milik sendiri berhasil, milik orang lain 404, tidak ada 404.
        var mine = list.Items[0].Id;
        Assert.Equal(HttpStatusCode.NoContent, (await h.Api.PostAsync($"/api/v1/notifications/{mine}/read", null, a.Token)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await h.Api.PostAsync($"/api/v1/notifications/{mine}/read", null, a.Token)).Status);
        ProblemAssert.Is(await h.Api.PostAsync($"/api/v1/notifications/{mine}/read", null, b.Token), HttpStatusCode.NotFound, ProblemTypes.NotFound);
        ProblemAssert.Is(await h.Api.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null, a.Token), HttpStatusCode.NotFound, ProblemTypes.NotFound);
        Assert.Equal(2, (await h.Api.GetAsync("/api/v1/notifications/summary", a.Token)).Ok().As<NotificationSummary>().UnreadCount);
        Assert.Equal(3, (await h.Api.GetAsync("/api/v1/notifications/summary", b.Token)).Ok().As<NotificationSummary>().UnreadCount);

        // Filter belum dibaca, lalu tandai semua (seluruh milik sendiri, bukan hanya yang tampak).
        Assert.Equal(2, (await h.Api.GetAsync("/api/v1/notifications?unread=true", a.Token)).Ok().As<PagedResult<NotificationItem>>().TotalCount);
        Assert.Equal(2, (await h.Api.PostAsync("/api/v1/notifications/read-all", null, a.Token)).Ok().As<MarkAllReadResult>().Marked);
        Assert.Equal(0, (await h.Api.GetAsync("/api/v1/notifications/summary", a.Token)).Ok().As<NotificationSummary>().UnreadCount);
        Assert.Equal(3, (await h.Api.GetAsync("/api/v1/notifications/summary", b.Token)).Ok().As<NotificationSummary>().UnreadCount);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Api.GetAsync("/api/v1/notifications/summary", null)).Status);
    }

    [Fact]
    public async Task Notifikasi_lama_hilang_dari_daftar_dan_hitungan_bila_izin_sumbernya_dicabut()
    {
        var h = await fixture.CreateIsolatedAsync();
        var role = await h.CreateRoleAsync(null, Permissions.CustomerPO.View);
        var user = await h.CreateReadyUserAsync(role.Id);
        await PublishAsync(h, Ne08(null));
        Assert.Equal(1, (await h.Api.GetAsync("/api/v1/notifications/summary", user.Token)).Ok().As<NotificationSummary>().UnreadCount);
        var id = (await h.Api.GetAsync("/api/v1/notifications", user.Token)).Ok().As<PagedResult<NotificationItem>>().Items.Single().Id;

        await using (var db = h.CreateDbContext())
        {
            await db.RolePermissions.Where(rp => rp.RoleId == role.Id).ExecuteDeleteAsync();
        }

        Assert.Equal(0, (await h.Api.GetAsync("/api/v1/notifications/summary", user.Token)).Ok().As<NotificationSummary>().UnreadCount);
        Assert.Empty((await h.Api.GetAsync("/api/v1/notifications", user.Token)).Ok().As<PagedResult<NotificationItem>>().Items);
        Assert.Equal(0, (await h.Api.GetAsync("/api/v1/notifications/counts", user.Token)).Ok().As<NotificationCounts>().All);
        ProblemAssert.Is(await h.Api.PostAsync($"/api/v1/notifications/{id}/read", null, user.Token), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    // ===== Pemeriksaan terjadwal =====

    private static async Task<NotificationJobOutcome> RunJobAsync(TestHost h)
    {
        using var scope = h.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationJob>().RunAsync(NotificationJobTrigger.Scheduled, CancellationToken.None);
    }

    private static async Task<int> CountAsync(TestHost h, string typeCode, Guid? recipient = null)
    {
        await using var db = h.CreateDbContext();
        return await db.Notifications.CountAsync(n => n.TypeCode == typeCode && (recipient == null || n.RecipientUserId == recipient));
    }

    [Fact]
    public async Task PO_terlambat_dan_jatuh_tempo_dibuat_sekali_per_episode_dan_dibuat_lagi_setelah_kondisi_hilang_lalu_muncul_kembali()
    {
        var h = await fixture.CreateIsolatedAsync();
        var w = await h.NewPoWorldAsync();
        var peer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        var po = await h.NewPoAsync(w, [w.Line(w.Small, 50, -10), w.Line(w.Medium, 30, 2), w.Line(w.Small, 40, 30)]);
        var lateLine = po.Lines.Single(l => l.DeliveryDate == Today(-10)).Id;

        var first = await RunJobAsync(h);
        Assert.False(first.Skipped);
        Assert.Equal(1, first.Created["NC-01"]);
        Assert.Equal(1, first.Created["NC-02"]);
        Assert.Equal(1, await CountAsync(h, "NC-01", peer.Id));
        Assert.Equal(1, await CountAsync(h, "NC-02", peer.Id));

        var item = (await h.Api.GetAsync("/api/v1/notifications?pageSize=10", peer.Token)).Ok().As<PagedResult<NotificationItem>>().Items.Single(i => i.TypeCode == "NC-01");
        Assert.Equal(po.Code, item.Params["code"]);
        Assert.Equal("1", item.Params["lines"]);
        Assert.Equal(po.Id.ToString(), item.Target!.Id);
        var due = (await h.Api.GetAsync("/api/v1/notifications?pageSize=10", peer.Token)).Ok().As<PagedResult<NotificationItem>>().Items.Single(i => i.TypeCode == "NC-02");
        Assert.Equal(Today(2).ToString("yyyy-MM-dd"), due.Params["dueDate"]);
        Assert.Equal("30", due.Params["qty"]);

        // Pemeriksaan ulang tidak menggandakan.
        var second = await RunJobAsync(h);
        Assert.Equal(0, second.Created["NC-01"]);
        Assert.Equal(1, await CountAsync(h, "NC-01", peer.Id));

        // Kondisi hilang (terkirim penuh) -> episode berakhir; kondisi muncul lagi -> episode baru + notifikasi baru.
        await h.UpdateLineAsync(lateLine, l => l.DeliveredQty = l.Qty);
        Assert.Equal(1, (await RunJobAsync(h)).EpisodesEnded);
        await using (var db = h.CreateDbContext())
        {
            Assert.Equal(0, await db.NotificationEpisodes.CountAsync(e => e.TypeCode == "NC-01" && e.EndedAt == null));
        }

        await h.UpdateLineAsync(lateLine, l => l.DeliveredQty = 0);
        Assert.Equal(1, (await RunJobAsync(h)).Created["NC-01"]);
        Assert.Equal(2, await CountAsync(h, "NC-01", peer.Id));
    }

    [Fact]
    public async Task Dua_pemeriksaan_bersamaan_tidak_pernah_menggandakan_notifikasi_atau_episode()
    {
        var h = await fixture.CreateIsolatedAsync();
        var w = await h.NewPoWorldAsync();
        var peer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        await h.NewPoAsync(w, [w.Line(w.Small, 50, -10)]);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RunJobAsync(h)));
        Assert.Contains(results, r => !r.Skipped);

        await using var db = h.CreateDbContext();
        Assert.Equal(1, await db.NotificationEpisodes.CountAsync(e => e.TypeCode == "NC-01" && e.EndedAt == null));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.TypeCode == "NC-01" && n.RecipientUserId == peer.Id));
        var perRecipient = await db.Notifications.Where(n => n.TypeCode == "NC-01").GroupBy(n => n.RecipientUserId).Select(g => g.Count()).ToListAsync();
        Assert.All(perRecipient, c => Assert.Equal(1, c));
    }

    [Fact]
    public async Task Jenis_kondisi_yang_dinonaktifkan_tidak_dibuat_dan_pemeriksaan_dicatat()
    {
        var h = await fixture.CreateIsolatedAsync();
        var w = await h.NewPoWorldAsync();
        await h.NewPoAsync(w, [w.Line(w.Small, 50, -10)]);
        var current = (await h.Api.GetAsync("/api/v1/notifications/settings", h.AdminToken)).Ok().As<NotificationSettingsDto>();
        (await h.Api.PutAsync("/api/v1/notifications/settings", new UpdateNotificationSettingsRequest(
            current.DueSoonDays, current.ScheduleTime, current.RetentionDays,
            current.Types.Select(t => t with { Enabled = t.TypeCode != "NC-01" }).ToList(), current.RowVersion), h.AdminToken)).Ok();

        var outcome = await RunJobAsync(h);
        Assert.Equal(0, outcome.Created["NC-01"]);
        Assert.Equal(0, await CountAsync(h, "NC-01"));

        await using var db = h.CreateDbContext();
        var run = await db.NotificationJobRuns.SingleAsync();
        Assert.Equal(NotificationJobTrigger.Scheduled, run.Trigger);
        Assert.Null(run.Error);
    }

    [Fact]
    public async Task WO_terlambat_dan_periode_gaji_perlu_ditutup_diperiksa_sesuai_definisi()
    {
        var h = await fixture.CreateIsolatedAsync();
        var peer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.WorkOrder.View, Permissions.Payroll.View)).Id);
        await using (var db = h.CreateDbContext())
        {
            var template = Today(-30);
            db.PayrollPeriods.Add(new Gms.Domain.Payroll.PayrollPeriod
            {
                Code = "GJ-TEST", StartDate = template, EndDate = Today(-1), Status = Gms.Shared.Enums.PayrollStatus.Open, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var outcome = await RunJobAsync(h);
        Assert.Equal(1, outcome.Created["NC-04"]);
        Assert.Equal(1, await CountAsync(h, "NC-04", peer.Id));
        Assert.Equal(0, outcome.Created["NC-03"]);
    }

    [Fact]
    public async Task Retensi_menghapus_notifikasi_lama_termasuk_yang_belum_dibaca_tanpa_memicu_notifikasi_ulang()
    {
        var h = await fixture.CreateIsolatedAsync();
        var w = await h.NewPoWorldAsync();
        var peer = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        await h.NewPoAsync(w, [w.Line(w.Small, 50, -10)]);
        await RunJobAsync(h);
        Assert.Equal(1, await CountAsync(h, "NC-01", peer.Id));

        await using (var db = h.CreateDbContext())
        {
            db.Notifications.Add(new Notification
            {
                RecipientUserId = peer.Id, TypeCode = "NE-08", Category = NotificationCategory.Penjualan, Params = "{}",
                SourceKey = "lama", CreatedAt = DateTimeOffset.UtcNow.AddDays(-100),
            });
            await db.Notifications.Where(n => n.TypeCode == "NC-01").ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, DateTimeOffset.UtcNow.AddDays(-120)));
            await db.SaveChangesAsync();
        }

        var outcome = await RunJobAsync(h);
        Assert.True(outcome.RetentionDeleted >= 2);
        Assert.Equal(0, await CountAsync(h, "NE-08", peer.Id));
        Assert.Equal(0, await CountAsync(h, "NC-01", peer.Id));

        // Episode aktif masih ada, jadi kondisi yang sama tidak menghasilkan notifikasi ulang (AC-NT-16).
        Assert.Equal(0, (await RunJobAsync(h)).Created["NC-01"]);
        Assert.Equal(0, await CountAsync(h, "NC-01", peer.Id));
    }
}
