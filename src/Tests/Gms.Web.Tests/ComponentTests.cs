using Microsoft.Extensions.DependencyInjection;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Web.Components.Layout;
using Gms.Web.Components.Shared;
using Gms.Web.Infrastructure.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Gms.Web.Tests;

/// <summary>Komponen dasar: struktur, aksesibilitas (label, caption, aria), dan state UI (_Foundation-UI.md 5-8).</summary>
public sealed class ComponentTests : WebTestBase
{
    [Fact]
    public void PageHeader_menampilkan_h1_breadcrumb_dengan_halaman_aktif_dan_aksi()
    {
        var cut = Render<PageHeader>(p => p
            .Add(x => x.Title, "Customer PO")
            .Add(x => x.Subtitle, "12 data")
            .Add(x => x.Breadcrumbs, [new PageHeader.Crumb("Penjualan"), new PageHeader.Crumb("Customer PO", "/purchase-orders"), new PageHeader.Crumb("PO-202611-0001")])
            .Add(x => x.Actions, b => b.AddMarkupContent(0, "<button id='aksi'>Tambah PO</button>")));

        Assert.Equal("Customer PO", cut.Find("h1").TextContent.Trim());
        Assert.Equal("Breadcrumb", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal(3, cut.FindAll("li").Count);
        Assert.Equal("page", cut.FindAll("li").Last().QuerySelector("span")!.GetAttribute("aria-current"));
        Assert.Equal("/purchase-orders", cut.Find("li:nth-child(2) a").GetAttribute("href"));
        Assert.Contains("12 data", cut.Markup);
        Assert.NotNull(cut.Find("#aksi"));
    }

    [Fact]
    public void PageHeader_tanpa_breadcrumb_dan_aksi_tidak_merender_bagian_kosong()
    {
        var cut = Render<PageHeader>(p => p.Add(x => x.Title, "Beranda"));

        Assert.Empty(cut.FindAll("nav"));
        Assert.Empty(cut.FindAll(".gms-pageheader-actions"));
    }

    [Fact]
    public void StatCard_menampilkan_angka_label_tautan_dan_penanda_ikon_untuk_nada_peringatan()
    {
        var plain = Render<StatTile>(p => p.Add(x => x.Label, "PO Terlambat").Add(x => x.Value, "7").Add(x => x.Description, "Perlu tindak lanjut").Add(x => x.Href, "/purchase-orders?terlambat=true"));
        var danger = Render<StatTile>(p => p.Add(x => x.Label, "WO Terlambat").Add(x => x.Value, "2").Add(x => x.Tone, StatTile.MetricTone.Danger));

        Assert.Equal("7", plain.Find(".gms-metric-value").TextContent);
        Assert.Equal("Lihat PO Terlambat", plain.Find("a").GetAttribute("aria-label"));
        Assert.Empty(plain.FindAll(".gms-metric-label svg"));
        Assert.Contains("danger", danger.Find("section").ClassName);
        Assert.NotEmpty(danger.FindAll(".gms-metric-label svg")); // tidak hanya warna: ada ikon
    }

    [Fact]
    public void EmptyState_menampilkan_aksi_sebagai_tautan_atau_tombol_dan_tanpa_aksi_bila_tidak_berhak()
    {
        var link = Render<EmptyState>(p => p.Add(x => x.Title, "Belum ada PO").Add(x => x.ActionText, "Tambah PO pertama").Add(x => x.ActionHref, "/purchase-orders/new"));
        var button = Render<EmptyState>(p => p.Add(x => x.Title, "Kosong").Add(x => x.ActionText, "Tambah"));
        var none = Render<EmptyState>(p => p.Add(x => x.Title, "Tidak ada hasil untuk filter ini"));

        Assert.Equal("/purchase-orders/new", link.Find("a").GetAttribute("href"));
        Assert.NotEmpty(button.FindAll("button.gms-btn"));
        Assert.Empty(none.FindAll("a, button"));
        Assert.Equal("status", none.Find(".gms-state").GetAttribute("role"));
    }

    [Fact]
    public void ErrorState_menampilkan_traceId_dan_tombol_coba_lagi_yang_memanggil_ulang()
    {
        var retried = 0;
        var cut = Render<ErrorState>(p => p
            .Add(x => x.Title, "Terjadi kesalahan pada server")
            .Add(x => x.Detail, "Coba lagi.")
            .Add(x => x.TraceId, "00-abc-123")
            .Add(x => x.OnRetry, EventCallback.Factory.Create(this, () => retried++)));

        Assert.Equal("alert", cut.Find(".gms-state").GetAttribute("role"));
        Assert.Contains("00-abc-123", cut.Find("code").TextContent);
        cut.Find("button.gms-btn").Click();
        Assert.Equal(1, retried);
    }

    [Fact]
    public void ErrorState_tanpa_OnRetry_tidak_menampilkan_tombol()
    {
        var cut = Render<ErrorState>(p => p.Add(x => x.Title, "Tidak ditemukan"));

        Assert.Empty(cut.FindAll("button.gms-btn"));
    }

    [Fact]
    public void SkeletonRows_memberi_tahu_pembaca_layar_dan_membangun_baris_sesuai_parameter()
    {
        var cut = Render<SkeletonRows>(p => p.Add(x => x.Rows, 4).Add(x => x.Columns, 3));

        var root = cut.Find(".gms-skeleton");
        Assert.Equal("true", root.GetAttribute("aria-busy"));
        Assert.Equal("status", root.GetAttribute("role"));
        Assert.Equal(4, cut.FindAll(".gms-skeleton-row").Count);
        Assert.Equal(12, cut.FindAll(".gms-skeleton-cell").Count);
    }

    [Theory]
    [InlineData(PoStatus.Draft, "Draft", "t-proc")]
    [InlineData(PoStatus.Confirmed, "Confirmed", "t-proc")]
    [InlineData(PoStatus.InProgress, "In Progress", "t-prog")]
    [InlineData(PoStatus.Completed, "Completed", "t-ok")]
    [InlineData(PoStatus.Cancelled, "Cancelled", "t-obs")]
    public void StatusBadge_PO_selalu_memuat_teks_dengan_warna_dari_peta_enam_kategori(PoStatus status, string label, string tone)
    {
        var cut = Render<StatusBadge>(p => p.Add(x => x.Value, status));

        var chip = cut.Find(".gms-tag");
        Assert.Contains(tone, chip.ClassName);
        Assert.Equal(label, chip.QuerySelector(":scope > span:last-child")!.TextContent.Trim());
        Assert.DoesNotContain("t-err", chip.ClassName); // Cancelled/Void bukan merah
    }

    [Fact]
    public void StatusBadge_Voided_dicoret_dan_kunci_teks_dan_tooltip_dapat_diganti()
    {
        var voided = Render<StatusBadge>(p => p.Add(x => x.Value, DocStatus.Voided));
        var late = Render<StatusBadge>(p => p.Add(x => x.Value, "late").Add(x => x.Tooltip, "Lewat 3 hari"));

        Assert.Contains("strike", voided.Find(".gms-tag").ClassName);
        Assert.Equal("Terlambat", late.Find(".gms-tag > span:last-child").TextContent.Trim());
        Assert.Equal("Lewat 3 hari", late.Find(".gms-tag").GetAttribute("title"));
    }

    [Theory]
    [InlineData(1, 25, 0, "Tidak ada data", 1)]
    [InlineData(1, 25, 12, "1–12 dari 12", 1)]
    [InlineData(2, 25, 60, "26–50 dari 60", 3)]
    [InlineData(3, 25, 60, "51–60 dari 60", 3)]
    [InlineData(1, 25, 12500, "1–25 dari 12.500", 500)]
    public void PagerBar_ringkasan_dan_jumlah_halaman(int page, int size, int total, string summary, int pages)
    {
        var cut = Render<PagerBar>(p => p.Add(x => x.Page, page).Add(x => x.PageSize, size).Add(x => x.TotalCount, total));

        Assert.Equal(summary, cut.Find("[aria-live]").TextContent);
        Assert.Contains($"Halaman {page} dari {pages}", cut.Markup);
        Assert.Equal(pages, cut.Instance.PageCount);
    }

    [Fact]
    public void PagerBar_menonaktifkan_tombol_di_tepi_dan_saat_sibuk_dan_meneruskan_halaman_baru()
    {
        var target = 0;
        var first = Render<PagerBar>(p => p.Add(x => x.Page, 1).Add(x => x.PageSize, 10).Add(x => x.TotalCount, 35).Add(x => x.PageChanged, EventCallback.Factory.Create<int>(this, v => target = v)));
        var buttons = first.FindAll("button.gms-btn");

        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.False(buttons[1].HasAttribute("disabled"));
        buttons[1].Click();
        Assert.Equal(2, target);

        var last = Render<PagerBar>(p => p.Add(x => x.Page, 4).Add(x => x.PageSize, 10).Add(x => x.TotalCount, 35));
        Assert.True(last.FindAll("button.gms-btn")[1].HasAttribute("disabled"));

        var busy = Render<PagerBar>(p => p.Add(x => x.Page, 2).Add(x => x.PageSize, 10).Add(x => x.TotalCount, 35).Add(x => x.Busy, true));
        Assert.All(busy.FindAll("button.gms-btn"), b => Assert.True(b.HasAttribute("disabled")));
    }

    [Fact]
    public void ProblemBanner_menjelaskan_penahan_bisnis_dengan_violations_dan_traceId()
    {
        var problem = new GmsProblemDetails
        {
            Type = ProblemTypes.RuleViolation, Status = 409, Title = "Aturan bisnis dilanggar.", Detail = "Qty melebihi batas.", TraceId = "00-xyz",
            Violations = [new RuleViolation("allocation.exceeded", "Navy / M", 21, 20, "Qty 21 melebihi batas 20.")],
        };

        var cut = Render<ProblemBanner>(p => p.Add(x => x.Problem, problem));

        Assert.Equal("alert", cut.Find(".gms-banner").GetAttribute("role"));
        Assert.Contains("warning", cut.Find(".gms-banner").ClassName);
        Assert.Contains("Navy / M", cut.Find(".gms-violations li").TextContent);
        Assert.Contains("melebihi batas 20", cut.Find(".gms-violations li").TextContent);
        Assert.Contains("00-xyz", cut.Find("code").TextContent);
        Assert.Empty(cut.FindAll("button.gms-btn")); // aturan bisnis: tidak ada "coba lagi"
    }

    [Fact]
    public void ProblemBanner_konkurensi_menawarkan_Muat_ulang_dan_galat_server_menawarkan_Coba_lagi()
    {
        var reloads = 0;
        var concurrency = Render<ProblemBanner>(p => p
            .Add(x => x.Problem, new GmsProblemDetails { Type = ProblemTypes.ConcurrencyConflict, Status = 409 })
            .Add(x => x.OnReload, EventCallback.Factory.Create(this, () => reloads++)));
        var server = Render<ProblemBanner>(p => p
            .Add(x => x.Problem, new GmsProblemDetails { Type = ProblemTypes.InternalError, Status = 500 })
            .Add(x => x.OnReload, EventCallback.Factory.Create(this, () => reloads++)));

        Assert.Contains("Data telah diubah pengguna lain", concurrency.Markup);
        Assert.Contains("Muat ulang", concurrency.Find("button.gms-btn").TextContent);
        Assert.Contains("Coba lagi", server.Find("button.gms-btn").TextContent);
        concurrency.Find("button.gms-btn").Click();
        server.Find("button.gms-btn").Click();
        Assert.Equal(2, reloads);
        Assert.Empty(Render<ProblemBanner>().FindAll(".gms-banner")); // tanpa galat: tidak ada banner
    }

    [Fact]
    public void FormSummary_menaut_ke_field_dengan_id_yang_konsisten_dan_memakai_label_tampil()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["lines[0].qty"] = ["Qty harus lebih dari 0."],
            ["customerId"] = ["Customer wajib dipilih.", "Customer tidak aktif."],
        };

        var cut = Render<FormSummary>(p => p.Add(x => x.Errors, errors).Add(x => x.Labels, new Dictionary<string, string> { ["customerId"] = "Customer" }));

        Assert.Equal("alert", cut.Find(".gms-formsummary").GetAttribute("role"));
        Assert.Equal(3, cut.FindAll("li").Count);
        Assert.Equal("#field-lines-0-qty", cut.FindAll("li a")[0].GetAttribute("href"));
        Assert.Equal("#field-customerid", cut.FindAll("li a")[1].GetAttribute("href"));
        Assert.Contains("Customer: Customer wajib dipilih.", cut.FindAll("li")[1].TextContent);
        Assert.Equal("field-lines-0-qty", FormSummary.FieldId("lines[0].qty"));
        Assert.Empty(Render<FormSummary>(p => p.Add(x => x.Errors, new Dictionary<string, string[]>())).FindAll(".gms-formsummary"));
    }

    [Fact]
    public void FormSummary_memindahkan_fokus_ke_ringkasan_saat_galat_muncul()
    {
        Render<FormSummary>(p => p.Add(x => x.Errors, new Dictionary<string, string[]> { ["a"] = ["salah"] }));

        JSInterop.VerifyFocusAsyncInvoke(); // fokus berpindah ke ringkasan (aksesibilitas)
    }

    [Fact]
    public void ImpactPreviewTable_menampilkan_sebelum_sesudah_batas_dan_status_ikon_dengan_teks()
    {
        var preview = new ImpactPreview(
            [
                new ImpactRow("Navy / M", "Kumulatif", 40m, 10m, null, true, null),
                new ImpactRow("Navy / L", "Kumulatif", 30m, 5m, 20m, false, "Output langkah berikutnya sudah 25."),
            ],
            CanProceed: false, [new RuleViolation("output.below-downstream", "Navy / L", 5, 25, "Output langkah berikutnya sudah 25.")], ["Entri akan di-void."]);

        var cut = Render<ImpactPreviewTable>(p => p.Add(x => x.Preview, preview));

        Assert.Equal("Dampak aksi ini", cut.Find("caption").TextContent);
        Assert.All(cut.FindAll("thead th"), th => Assert.Equal("col", th.GetAttribute("scope")));
        var rows = cut.FindAll("tbody tr");
        Assert.Contains("Aman", rows[0].TextContent);
        Assert.Contains("Melanggar", rows[1].TextContent);
        Assert.Contains("violation", rows[1].ClassName);
        Assert.Contains("40", rows[0].TextContent);
        Assert.Contains("—", rows[0].TextContent); // batas tidak ada
        Assert.Contains("Aksi tidak dapat dilanjutkan", cut.Find(".gms-banner").TextContent);
        Assert.Contains("Entri akan di-void.", cut.Find(".gms-note").TextContent);
    }

    [Fact]
    public void ImpactPreviewTable_aman_tidak_menampilkan_banner_pelanggaran_dan_null_tidak_merender_apa_pun()
    {
        var ok = Render<ImpactPreviewTable>(p => p.Add(x => x.Preview, new ImpactPreview([new ImpactRow("S", "Stok", 1m, 2m, null, true, null)], true, [], [])));

        Assert.Empty(ok.FindAll(".gms-banner"));
        Assert.Empty(Render<ImpactPreviewTable>().Markup.Trim());
    }

    private static IReadOnlyList<NavCategory> SampleCatalog() =>
    [
        new("sales", "Penjualan", "file-text",
        [
            new("Customer PO",
            [
                new("po.list", "Daftar PO", "Pesanan customer", "/purchase-orders", Permissions.CustomerPO.View),
                new("po.new", "Buat PO", "Catat pesanan baru", "/purchase-orders/new", Permissions.CustomerPO.Create, NavKind.Action),
            ]),
        ]),
        new("payroll", "Penggajian", "wallet", [new("Payroll", [new("payroll.list", "Periode Gaji", "Periode", "/payroll", Permissions.Payroll.View)])]),
    ];

    [Fact]
    public void TopNav_menampilkan_Beranda_dan_kategori_dalam_nav_bernama_dan_panel_tertutup_awalnya()
    {
        var cut = Render<TopNav>(p => p.Add(x => x.Catalog, SampleCatalog()));

        Assert.Equal("Navigasi utama", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal("Beranda", cut.Find("nav > a").TextContent.Trim());
        Assert.Equal(["Penjualan", "Penggajian"], cut.FindAll("nav > button").Select(b => b.TextContent.Trim()));
        Assert.All(cut.FindAll("nav > button"), b => Assert.Equal("false", b.GetAttribute("aria-expanded")));
        Assert.Empty(cut.FindAll(".gms-mega"));
    }

    [Fact]
    public void TopNav_dibuka_dengan_klik_satu_panel_saja_membedakan_halaman_dan_aksi_dan_menutup_dengan_Esc_scrim_atau_klik_ulang()
    {
        var cut = Render<TopNav>(p => p.Add(x => x.Catalog, SampleCatalog()));

        cut.FindAll("nav > button")[0].Click();

        Assert.Equal("true", cut.FindAll("nav > button")[0].GetAttribute("aria-expanded"));
        var panel = cut.Find(".gms-mega");
        Assert.Equal("region", panel.GetAttribute("role"));
        var items = panel.QuerySelectorAll("a.gms-mitem").ToList();
        Assert.Equal(["Daftar PO", "Buat PO +"], items.Select(x => x.QuerySelector("b")!.TextContent.Trim()));
        Assert.Empty(items[0].QuerySelectorAll(".gms-tile.t")); // item halaman: tile netral
        Assert.Single(items[1].QuerySelectorAll(".gms-tile.t")); // item aksi: tile tint brand + tanda +
        Assert.NotEmpty(panel.QuerySelectorAll(".gms-mgrp h3 .gms-icon")); // grup berikon

        cut.FindAll("nav > button")[1].Click(); // kategori lain menggantikan, bukan menumpuk
        Assert.Single(cut.FindAll(".gms-mega"));
        Assert.Equal("Penggajian", cut.Find(".gms-mega").GetAttribute("aria-label"));

        cut.Find("nav").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".gms-mega"));

        cut.FindAll("nav > button")[0].Click();
        cut.Find(".gms-scrim").Click();
        Assert.Empty(cut.FindAll(".gms-mega"));

        cut.FindAll("nav > button")[0].Click();
        cut.FindAll("nav > button")[0].Click();
        Assert.Empty(cut.FindAll(".gms-mega"));
    }

    [Fact]
    public void TopNav_menandai_kategori_aktif_dan_item_aktif_dengan_aria_current()
    {
        Nav.NavigateTo("/purchase-orders/new");
        var cut = Render<TopNav>(p => p.Add(x => x.Catalog, SampleCatalog()));

        Assert.Contains("on", cut.FindAll("nav > button")[0].ClassName);
        Assert.DoesNotContain("on", cut.FindAll("nav > button")[1].ClassName ?? string.Empty);

        cut.FindAll("nav > button")[0].Click();
        var current = cut.FindAll("a.gms-mitem").Where(a => a.GetAttribute("aria-current") == "page").Select(a => a.GetAttribute("href"));
        Assert.Contains("/purchase-orders/new", current);
    }

    [Fact]
    public void AppHeader_memuat_logo_pencarian_terbatas_izin_bahasa_notifikasi_dan_menu_pengguna_dengan_logout_berantiforgery()
    {
        SignIn(Permissions.CustomerPO.View);
        var catalog = NavCatalog.VisibleFor(u => u == Permissions.CustomerPO.View);
        var cut = Render<AppHeader>(p => p.Add(x => x.Catalog, catalog));

        Assert.Equal("GMS, ke Beranda", cut.Find("a.gms-logo").GetAttribute("aria-label"));
        Assert.NotNull(cut.Find("[role=search] input"));
        Assert.NotNull(cut.Find("button[aria-label^='Bahasa']"));
        Assert.NotNull(cut.Find("button[aria-label='Notifikasi']"));
        Assert.Contains("Budi", cut.Find("button[aria-label^='Menu pengguna']").GetAttribute("aria-label"));

        // Pencarian hanya memuat entri yang boleh dilihat dan menyebut jalur menu (maks. 7).
        cut.Find("[role=search] input").Input("po");
        var results = cut.FindAll(".gms-sresults a").Select(a => a.QuerySelector("b")!.TextContent.Trim()).ToList();
        Assert.Contains("Daftar PO", results);
        Assert.DoesNotContain(results, r => r.Contains("Work Order") || r.Contains("Buat PO")); // tanpa izin Create/WorkOrder
        Assert.InRange(results.Count, 1, 7);
        cut.Find("[role=search] input").Input("zzzz-tidak-ada");
        Assert.Contains("Tidak ada hasil", cut.Find(".gms-sresults").TextContent);

        cut.Find("button[aria-label^='Menu pengguna']").Click();
        var form = cut.Find("form[action='/account/logout']");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Contains("Ganti Password", cut.Markup);

        cut.Find("button[aria-label^='Bahasa']").Click(); // satu panel pada satu waktu
        Assert.Empty(cut.FindAll("form[action='/account/logout']"));
        Assert.Contains("/account/language?culture=en-US", cut.Markup);
    }

    private (Gms.Web.Services.Tabs.TabManager Tabs, Microsoft.AspNetCore.Components.NavigationManager Nav) NewTabs()
    {
        var tabs = Services.GetRequiredService<Gms.Web.Services.Tabs.TabManager>();
        tabs.EnsureHome(typeof(Gms.Web.Components.Pages.Home));
        return (tabs, Nav);
    }

    private static void Open(Gms.Web.Services.Tabs.TabManager tabs, Microsoft.AspNetCore.Components.NavigationManager nav, string path, Type? page = null)
    {
        nav.NavigateTo(path);
        tabs.Sync(page ?? typeof(Gms.Web.Components.Pages.Home), new Dictionary<string, object?>(), nav.Uri);
    }

    [Fact]
    public void TabManager_membuka_memfokuskan_dan_menyatukan_detail_dengan_mode_ubah_dalam_satu_tab()
    {
        var (tabs, nav) = NewTabs();
        Assert.Equal(["home"], tabs.Tabs.Select(t => t.Id));
        Assert.True(tabs.Tabs[0].Pinned);

        Open(tabs, nav, "/purchase-orders");
        Open(tabs, nav, "/purchase-orders/00000000-0000-0000-0000-000000000001");
        Assert.Equal(["home", "purchase-orders", "purchase-orders:00000000-0000-0000-0000-000000000001"], tabs.Tabs.Select(t => t.Id));

        // Mode Ubah memakai tab dokumen yang sama; membuka lagi memfokuskan, bukan menambah.
        Open(tabs, nav, "/purchase-orders/00000000-0000-0000-0000-000000000001/edit");
        Open(tabs, nav, "/purchase-orders");
        Assert.Equal(3, tabs.Tabs.Count);
        Assert.Equal("purchase-orders", tabs.ActiveId);
        Assert.Equal("/purchase-orders", tabs.Tabs.Single(t => t.Id == "purchase-orders").Url);
        Assert.Equal("/purchase-orders/00000000-0000-0000-0000-000000000001/edit", tabs.Tabs[2].Url);

        // Dokumen baru: satu tab per layar.
        Open(tabs, nav, "/purchase-orders/new");
        Open(tabs, nav, "/purchase-orders/new");
        Assert.Equal(1, tabs.Tabs.Count(t => t.Id == "purchase-orders:new"));
    }

    [Fact]
    public void TabManager_menolak_tab_ke_11_tanpa_menutup_tab_lama_diam_diam_dan_beranda_tidak_dapat_ditutup()
    {
        var (tabs, nav) = NewTabs();
        for (var i = 1; i <= 9; i++)
        {
            Open(tabs, nav, $"/work-orders/00000000-0000-0000-0000-00000000000{i}");
        }

        Assert.Equal(10, tabs.Tabs.Count);
        Open(tabs, nav, "/work-orders/00000000-0000-0000-0000-00000000000a");

        Assert.Equal(10, tabs.Tabs.Count);
        Assert.True(tabs.LimitReached);
        Assert.DoesNotContain(tabs.Tabs, t => t.Id.EndsWith(":00000000-0000-0000-0000-00000000000a"));
        Assert.EndsWith("/work-orders/00000000-0000-0000-0000-000000000009", nav.Uri); // kembali ke tab aktif

        tabs.Close("home");
        Assert.Contains(tabs.Tabs, t => t.Id == "home");
        tabs.Close("work-orders:00000000-0000-0000-0000-000000000009");
        Assert.Equal(9, tabs.Tabs.Count);
        Assert.NotEqual("work-orders:00000000-0000-0000-0000-000000000009", tabs.ActiveId); // tab tetangga menjadi aktif
    }

    [Fact]
    public void TabManager_menetapkan_judul_dirty_dan_alert_lewat_konteks_tab_dan_memetakan_query_ke_parameter_halaman()
    {
        var (tabs, nav) = NewTabs();
        Open(tabs, nav, "/purchase-orders?terlambat=1&jatuhTempo7=true", typeof(Gms.Web.Components.Pages.Sales.PoListPage));
        var tab = tabs.Active!;

        tab.Context.SetTitle("Customer PO");
        tab.Context.SetDirty(true);
        tab.Context.SetAlert(Gms.Web.Services.Tabs.TabAlertKind.Error, "Ada baris terlambat");

        Assert.Equal(("Customer PO", true, Gms.Web.Services.Tabs.TabAlertKind.Error, "Ada baris terlambat"), (tab.Title, tab.Dirty, tab.Alert, tab.AlertText));
        Assert.True(tabs.HasDirty);
        Assert.Equal(true, tab.Parameters["QTerlambat"]);
        Assert.Equal(true, tab.Parameters["QJatuhTempo7"]);
        Assert.False(tab.Parameters.ContainsKey("QSisaRencana"));

        tabs.Reload(tab.Id); // muat ulang membuang state dan status belum disimpan
        Assert.False(tab.Dirty);
        Assert.Equal(1, tab.Generation);
    }

    [Fact]
    public void TabBar_menampilkan_titik_belum_disimpan_ikon_alert_dan_meminta_konfirmasi_saat_menutup_tab_dirty()
    {
        var (tabs, nav) = NewTabs();
        Open(tabs, nav, "/purchase-orders/new");
        tabs.Active!.Context.SetTitle("Buat PO");
        tabs.Active.Context.SetDirty(true);
        tabs.Active.Context.SetAlert(Gms.Web.Services.Tabs.TabAlertKind.Warning, "Peringatan");

        var cut = Render<TabBar>();

        Assert.Equal("Tab dokumen", cut.Find("[role=tablist]").GetAttribute("aria-label"));
        Assert.Contains("belum disimpan", cut.Markup);
        Assert.NotEmpty(cut.FindAll(".gms-dirty"));
        Assert.NotEmpty(cut.FindAll(".gms-alert-warn"));
        Assert.Contains("Semua tab (2/10)", cut.Markup);

        cut.Find("button[aria-label='Tutup Buat PO']").Click();
        Assert.Contains("Tutup tab tanpa menyimpan?", cut.Find(".gms-dialog-wrap h2").TextContent);
        Assert.Equal(2, tabs.Tabs.Count); // belum ditutup sebelum dikonfirmasi
        cut.Find(".gms-dialog-footer button").Click(); // "Tetap di sini"
        Assert.Empty(cut.FindAll(".gms-dialog-wrap"));
        Assert.Equal(2, tabs.Tabs.Count);
    }

    [Fact]
    public void TextField_dan_LookupSelect_menampilkan_galat_terhubung_ke_field_dan_menggantikan_petunjuk()
    {
        var text = Render<TextField>(p => p.Add(x => x.Label, "Kode").Add(x => x.Hint, "Huruf besar").Add(x => x.Error, "Kode sudah dipakai"));
        var input = text.Find("input");

        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        var error = text.Find("small.gms-field-error");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal(error.Id, input.GetAttribute("aria-describedby"));
        Assert.Contains("has-error", text.Find(".gms-form").ClassName);
        Assert.DoesNotContain("Huruf besar", text.Markup); // galat menggantikan petunjuk

        var clean = Render<TextField>(p => p.Add(x => x.Label, "Kode").Add(x => x.Hint, "Huruf besar"));
        Assert.Null(clean.Find("input").GetAttribute("aria-invalid"));
        Assert.Contains("Huruf besar", clean.Markup);

        var select = Render<LookupSelect>(p => p.Add(x => x.Label, "Satuan").Add(x => x.Error, "Pilih satuan"));
        Assert.Equal("true", select.Find("select").GetAttribute("aria-invalid"));
        Assert.Equal("Pilih satuan", select.Find("small.gms-field-error").TextContent.Trim());
    }
}
