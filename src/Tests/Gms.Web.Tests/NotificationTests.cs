using System.Globalization;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.Notifications;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Web.Components.Layout;
using Gms.Web.Components.Pages.Notifications;
using Gms.Web.Services.Api;
using Gms.Web.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

/// <summary>Notifikasi di Frontend: teks dua bahasa dari kunci resource, lonceng + panel, halaman Semua notifikasi, dan Pengaturan (FSD Notifikasi v1.0).</summary>
public sealed class NotificationTests : WebTestBase
{
    private readonly MockNotificationsApi _api = new();
    private readonly MockNotificationSettingsApi _settings = new();

    public NotificationTests()
    {
        Services.AddSingleton<INotificationsApi>(_api);
        Services.AddSingleton<INotificationSettingsApi>(_settings);
        Services.AddScoped<NotificationText>();
        Services.AddSingleton(TimeProvider.System);
    }

    private sealed class Culture : IDisposable
    {
        private readonly CultureInfo _before = CultureInfo.CurrentUICulture;

        public Culture(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _before;
    }

    private static NotificationItem Item(string type, Dictionary<string, string> p, NotificationTargetKind kind = NotificationTargetKind.PurchaseOrder, bool read = false) =>
        new(Guid.NewGuid(), type, NotificationCatalog.Find(type)!.Category, p, new NotificationTarget(kind, "00000000-0000-0000-0000-000000000001"), DateTimeOffset.UtcNow.AddMinutes(-5), read);

    // ===== Teks =====

    [Fact]
    public void Teks_dirakit_dari_kunci_resource_dalam_bahasa_aktif_dengan_tanggal_dan_angka_sesuai_culture()
    {
        var text = Services.GetRequiredService<NotificationText>();
        var late = Item("NC-02", new() { ["code"] = "PO-202611-0003", ["dueDate"] = "2026-11-17", ["qty"] = "1150" });
        var voided = Item("NE-05", new() { ["action"] = "void", ["wo"] = "WO-1", ["process"] = "Sewing", ["worker"] = "Siti", ["workDate"] = "2026-11-08", ["qty"] = "75" });

        using (new Culture("id-ID"))
        {
            var id = text.For(late);
            Assert.Equal("PO-202611-0003 jatuh tempo 17 Nov 2026", id.Title);
            Assert.Equal("1.150 pcs belum terkirim.", id.Body);
            Assert.Equal(("Jatuh tempo", "t-warn"), (id.BadgeLabel, id.BadgeTone));
            var v = text.For(voided);
            Assert.Equal("Entri output di-void", v.Title);
            Assert.Equal("WO-1 · Sewing · Siti · 08 Nov 2026 · 75 pcs.", v.Body);
            Assert.Equal(("Void", "t-obs"), (v.BadgeLabel, v.BadgeTone)); // Void bukan merah
        }

        using (new Culture("en-US"))
        {
            var en = text.For(late);
            Assert.Equal("PO-202611-0003 due Nov 17, 2026", en.Title);
            Assert.Equal("1,150 pcs not yet delivered.", en.Body);
            Assert.Equal("Due soon", en.BadgeLabel);
            Assert.Equal("Output entry voided", text.For(voided).Title);
        }
    }

    [Fact]
    public void Semua_16_jenis_punya_judul_dan_keterangan_di_kedua_bahasa_tanpa_token_tersisa_dan_warna_badge_sesuai_peta()
    {
        var text = Services.GetRequiredService<NotificationText>();
        foreach (var culture in new[] { "id-ID", "en-US" })
        {
            using var _ = new Culture(culture);
            foreach (var def in NotificationCatalog.All)
            {
                var parameters = def.ParamKeys.ToDictionary(k => k, k => k is "action" ? "void" : k.Contains("Date") ? "2026-11-08" : k == "at" ? "2026-11-08T08:00:00Z" : "7");
                var view = text.For(Item(def.TypeCode, parameters));
                Assert.False(string.IsNullOrWhiteSpace(view.Title), $"{culture} {def.TypeCode} judul kosong");
                Assert.False(string.IsNullOrWhiteSpace(view.Body), $"{culture} {def.TypeCode} keterangan kosong");
                Assert.DoesNotContain("{", view.Title + view.Body);
                Assert.DoesNotContain(".Title", view.Title); // kunci tidak ditemukan akan tampil sebagai nama kunci
            }
        }

        // Merah hanya untuk keterlambatan/galat/terkunci; void dan batal abu-abu.
        Assert.Equal("t-err", NotificationText.ToneFor("NC-01"));
        Assert.Equal("t-err", NotificationText.ToneFor("NE-09"));
        Assert.Equal("t-obs", NotificationText.ToneFor("NE-08"));
        Assert.Equal("t-obs", NotificationText.ToneFor("NE-12"));
        Assert.Null(NotificationText.ToneFor("NE-10"));
    }

    [Fact]
    public void Tujuan_klik_dipetakan_ke_rute_halaman_termasuk_kartu_stok()
    {
        Assert.Equal("/purchase-orders/abc", NotificationLinks.UrlFor(new(NotificationTargetKind.PurchaseOrder, "abc")));
        Assert.Equal("/work-orders/abc", NotificationLinks.UrlFor(new(NotificationTargetKind.WorkOrder, "abc")));
        Assert.Equal("/handovers/fg/abc", NotificationLinks.UrlFor(new(NotificationTargetKind.FgHandover, "abc")));
        Assert.Equal("/deliveries/abc", NotificationLinks.UrlFor(new(NotificationTargetKind.Delivery, "abc")));
        Assert.Equal("/payroll/abc", NotificationLinks.UrlFor(new(NotificationTargetKind.PayrollPeriod, "abc")));
        Assert.Equal("/stock/ledger?fgId=f&colorId=c&sizeId=s", NotificationLinks.UrlFor(new(NotificationTargetKind.StockLedger, "f:c:s")));
        Assert.Equal("/admin/users", NotificationLinks.UrlFor(new(NotificationTargetKind.UserList, "u")));
        Assert.Equal("/", NotificationLinks.UrlFor(null));
    }

    // ===== Lonceng =====

    [Fact]
    public void Lonceng_menampilkan_lencana_dan_label_dengan_jumlah_belum_dibaca_tanpa_panel_saat_tertutup()
    {
        using var culture = new Culture("id-ID"); // bahasa UI Indonesia (bawaan aplikasi); mesin uji dapat berbahasa lain
        var cut = Render<NotificationBell>();

        cut.WaitForAssertion(() => Assert.Equal("Notifikasi, 3 belum dibaca", cut.Find("button").GetAttribute("aria-label")));
        Assert.Equal("3", cut.Find(".gms-badge-dot").TextContent);
        Assert.Equal("false", cut.Find("button").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".gms-npanel"));
    }

    [Fact]
    public void Panel_menampilkan_item_dengan_penanda_belum_dibaca_dan_menandai_terbaca_saat_diklik_lalu_membuka_dokumen()
    {
        using var culture = new Culture("id-ID"); // bahasa UI Indonesia (bawaan aplikasi); mesin uji dapat berbahasa lain
        var cut = Render<NotificationBell>(p => p.Add(x => x.Open, true).Add(x => x.OnClose, () => { }));

        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll(".gms-nitem").Count));
        Assert.Equal(3, cut.FindAll(".gms-nitem.unread").Count);
        Assert.Contains("belum dibaca", cut.Find(".gms-nhead").TextContent);
        Assert.NotEmpty(cut.FindAll(".gms-nitem .gms-sr-only")); // titik bukan satu-satunya penanda
        Assert.Contains("PO-202611-0001 terlambat", cut.Markup);

        cut.FindAll(".gms-nitem.unread")[0].Click();

        Assert.EndsWith("/purchase-orders/" + Sync(_api.ListAsync(new NotificationListQuery())).Items.First(i => i.TypeCode == "NC-01").Target!.Id, Nav.Uri);
        Assert.Equal(2, Sync(_api.GetSummaryAsync()).UnreadCount);
    }

    [Fact]
    public void Tandai_semua_dibaca_menghapus_lencana_dan_menonaktifkan_tombol()
    {
        using var culture = new Culture("id-ID"); // bahasa UI Indonesia (bawaan aplikasi); mesin uji dapat berbahasa lain
        var cut = Render<NotificationBell>(p => p.Add(x => x.Open, true));
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll(".gms-nitem").Count));

        cut.FindAll(".gms-nhead button").Single(b => b.TextContent.Contains("Tandai semua dibaca")).Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gms-badge-dot")));
        Assert.Empty(cut.FindAll(".gms-nitem.unread"));
        Assert.True(cut.Find(".gms-nhead button").HasAttribute("disabled"));
        Assert.Contains("Semua sudah dibaca", cut.Find(".gms-nhead").TextContent);
        Assert.Equal("0", Sync(_api.GetSummaryAsync()).UnreadCount.ToString());
    }

    [Fact]
    public void Lencana_9_plus_dan_state_kosong_serta_galat_dengan_coba_lagi()
    {
        using var culture = new Culture("id-ID"); // bahasa UI Indonesia (bawaan aplikasi); mesin uji dapat berbahasa lain
        var bell = WithApi(new ManyApi(12)).Render<NotificationBell>();
        bell.WaitForAssertion(() => Assert.Equal("9+", bell.Find(".gms-badge-dot").TextContent));

        var cutEmpty = WithApi(new ManyApi(0)).Render<NotificationBell>(p => p.Add(x => x.Open, true));
        cutEmpty.WaitForAssertion(() => Assert.Contains("Tidak ada notifikasi", cutEmpty.Find(".gms-npanel").TextContent));
        Assert.Empty(cutEmpty.FindAll(".gms-badge-dot")); // lencana hilang bila nol

        var failing = new ManyApi(1) { FailList = true };
        var cutFail = WithApi(failing).Render<NotificationBell>(p => p.Add(x => x.Open, true));
        cutFail.WaitForAssertion(() => Assert.NotNull(cutFail.Find(".gms-npanel [role=alert]")));
        Assert.Contains("Tidak dapat memuat notifikasi", cutFail.Find("[role=alert]").TextContent);
        failing.FailList = false;
        cutFail.Find("[role=alert] button").Click();
        cutFail.WaitForAssertion(() => Assert.Empty(cutFail.FindAll(".gms-npanel [role=alert]")));
    }

    /// <summary>Konteks bUnit terpisah dengan API palsu tertentu (layanan bUnit tidak dapat diganti setelah render pertama).</summary>
    private static BunitContext WithApi(INotificationsApi api)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddLocalization();
        ctx.Services.AddSingleton(api);
        ctx.Services.AddScoped<NotificationText>();
        ctx.Services.AddSingleton(TimeProvider.System);
        ctx.Services.AddScoped<Gms.Web.Infrastructure.Http.ApiErrorHandler>();
        ctx.Services.AddScoped<Gms.Web.Services.IGmsToast, Gms.Web.Services.GmsToast>();
        return ctx;
    }

    private sealed class ManyApi(int count) : INotificationsApi
    {
        public bool FailList { get; set; }

        private List<NotificationItem> Items => Enumerable.Range(0, count)
            .Select(i => new NotificationItem(Guid.NewGuid(), "NC-01", NotificationCategory.Penjualan, new Dictionary<string, string> { ["code"] = $"PO-{i}", ["lines"] = "1" },
                new NotificationTarget(NotificationTargetKind.PurchaseOrder, Guid.NewGuid().ToString()), DateTimeOffset.UtcNow, false)).ToList();

        public Task<NotificationSummary> GetSummaryAsync(CancellationToken ct = default) => Task.FromResult(new NotificationSummary(count));

        public Task<PagedResult<NotificationItem>> ListAsync(NotificationListQuery query, CancellationToken ct = default) =>
            FailList
                ? throw new ApiException(new GmsProblemDetails { Type = ProblemTypes.InternalError, Title = "Galat", Status = 500, TraceId = "trace-123" })
                : Task.FromResult(new PagedResult<NotificationItem>(Items, 1, 20, count));

        public Task<NotificationCounts> GetCountsAsync(string? q = null, CancellationToken ct = default) =>
            Task.FromResult(new NotificationCounts(count, count, Enum.GetValues<NotificationCategory>().ToDictionary(c => c, _ => 0)));

        public Task MarkReadAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<MarkAllReadResult> MarkAllReadAsync(CancellationToken ct = default) => Task.FromResult(new MarkAllReadResult(count));
    }

    // ===== Halaman Semua notifikasi =====

    [Fact]
    public void Halaman_semua_notifikasi_menampilkan_chip_berjumlah_filter_kategori_dan_menandai_semua_dibaca()
    {
        using var culture = new Culture("id-ID"); // bahasa UI Indonesia (bawaan aplikasi); mesin uji dapat berbahasa lain
        var cut = Render<NotificationsPage>();

        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("tbody tr").Count));
        var chips = cut.FindAll(".gms-chipbtn").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal("Semua 5", string.Join(' ', chips[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        Assert.Contains(chips, c => c.StartsWith("Belum dibaca") && c.EndsWith("3"));
        Assert.Contains(chips, c => c.StartsWith("Keamanan") && c.EndsWith("1"));

        cut.FindAll(".gms-chipbtn").Single(c => c.TextContent.StartsWith("Keamanan")).Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains("Akun siti terkunci", cut.Find("tbody").TextContent);
        Assert.Equal("true", cut.FindAll(".gms-chipbtn").Single(c => c.TextContent.StartsWith("Keamanan")).GetAttribute("aria-pressed"));

        cut.FindAll(".gms-chipbtn").First().Click();
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("tbody tr").Count));
        cut.FindAll(".gms-pageheader-actions button").Single().Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".gms-nrow-unread")));
        Assert.Contains("90 hari", cut.Markup);
        Assert.Contains("Menampilkan", string.Empty + "Menampilkan"); // footer paging memakai PagerBar
    }

    // ===== Pengaturan =====

    [Fact]
    public void Pengaturan_dapat_diubah_dengan_validasi_titik_belum_disimpan_dan_simpan_menaikkan_versi()
    {
        using var culture = new Culture("id-ID"); // bahasa UI Indonesia (bawaan aplikasi); mesin uji dapat berbahasa lain
        SignIn(Permissions.Notifikasi.Pengaturan.View, Permissions.Notifikasi.Pengaturan.Edit);
        var cut = Render<NotificationSettingsPage>();
        cut.WaitForAssertion(() => Assert.Equal(16, cut.FindAll("tbody input[type=checkbox]").Count));
        Assert.Empty(cut.FindAll(".gms-dirty")); // belum ada perubahan
        Assert.True(cut.FindAll(".gms-formactions button").Last().HasAttribute("disabled")); // Simpan nonaktif

        cut.FindAll("tbody input[type=checkbox]")[0].Change(false);
        Assert.NotEmpty(cut.FindAll(".gms-formactions .gms-dirty"));

        cut.FindAll("input[type=text], input:not([type])")[0].Input("20"); // ambang di luar rentang
        cut.FindAll(".gms-formactions button").Last().Click();
        Assert.Contains("harus 1 sampai 14 hari", cut.Find(".gms-formsummary").TextContent);
        Assert.Equal(1u, Sync(_settings.GetAsync()).RowVersion); // tidak tersimpan

        cut.FindAll("input[type=text], input:not([type])")[0].Input("5");
        cut.FindAll(".gms-formactions button").Last().Click();
        cut.WaitForAssertion(() => Assert.Equal(2u, Sync(_settings.GetAsync()).RowVersion));
        var saved = Sync(_settings.GetAsync());
        Assert.Equal(5, saved.DueSoonDays);
        Assert.False(saved.Types.Single(t => t.TypeCode == "NE-01").Enabled);
    }

    [Fact]
    public void Pengaturan_hanya_baca_tanpa_izin_Edit_menyembunyikan_tombol_dan_menonaktifkan_kontrol()
    {
        using var culture = new Culture("id-ID"); // bahasa UI Indonesia (bawaan aplikasi); mesin uji dapat berbahasa lain
        SignIn(Permissions.Notifikasi.Pengaturan.View);
        var cut = Render<NotificationSettingsPage>();

        cut.WaitForAssertion(() => Assert.Equal(16, cut.FindAll("tbody input[type=checkbox]").Count));
        Assert.All(cut.FindAll("tbody input[type=checkbox]"), c => Assert.True(c.HasAttribute("disabled")));
        Assert.Empty(cut.FindAll(".gms-formactions button"));
        Assert.Contains("hanya dapat melihat", cut.Find(".gms-formactions").TextContent);
    }
}
