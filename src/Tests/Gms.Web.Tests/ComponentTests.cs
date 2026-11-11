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

        Assert.Equal("Customer PO", cut.Find("h1").TextContent);
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
        var plain = Render<StatCard>(p => p.Add(x => x.Label, "PO Terlambat").Add(x => x.Value, "7").Add(x => x.Description, "Perlu tindak lanjut").Add(x => x.Href, "/purchase-orders?terlambat=true"));
        var danger = Render<StatCard>(p => p.Add(x => x.Label, "WO Terlambat").Add(x => x.Value, "2").Add(x => x.Tone, StatCard.MetricTone.Danger));

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
        Assert.NotEmpty(button.FindAll("button.ant-btn"));
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
        cut.Find("button.ant-btn").Click();
        Assert.Equal(1, retried);
    }

    [Fact]
    public void ErrorState_tanpa_OnRetry_tidak_menampilkan_tombol()
    {
        var cut = Render<ErrorState>(p => p.Add(x => x.Title, "Tidak ditemukan"));

        Assert.Empty(cut.FindAll("button.ant-btn"));
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
    [InlineData(PoStatus.Draft, "Draft", "neutral")]
    [InlineData(PoStatus.Confirmed, "Confirmed", "cyan")]
    [InlineData(PoStatus.InProgress, "In Progress", "blue")]
    [InlineData(PoStatus.Completed, "Completed", "success")]
    [InlineData(PoStatus.Cancelled, "Cancelled", "danger")]
    public void StatusTag_PO_selalu_memuat_ikon_dan_teks(PoStatus status, string label, string tone)
    {
        var cut = Render<StatusTag>(p => p.Add(x => x.Value, status));

        var chip = cut.Find(".gms-tag");
        Assert.Contains(tone, chip.ClassName);
        Assert.Equal(label, chip.QuerySelector(":scope > span:last-child")!.TextContent.Trim());
        Assert.NotNull(chip.QuerySelector("svg")); // ikon AntDesign
    }

    [Fact]
    public void StatusTag_Voided_dicoret_dan_kunci_teks_dan_tooltip_dapat_diganti()
    {
        var voided = Render<StatusTag>(p => p.Add(x => x.Value, DocStatus.Voided));
        var late = Render<StatusTag>(p => p.Add(x => x.Value, "late").Add(x => x.Tooltip, "Lewat 3 hari"));

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
        var buttons = first.FindAll("button.ant-btn");

        Assert.True(buttons[0].HasAttribute("disabled"));
        Assert.False(buttons[1].HasAttribute("disabled"));
        buttons[1].Click();
        Assert.Equal(2, target);

        var last = Render<PagerBar>(p => p.Add(x => x.Page, 4).Add(x => x.PageSize, 10).Add(x => x.TotalCount, 35));
        Assert.True(last.FindAll("button.ant-btn")[1].HasAttribute("disabled"));

        var busy = Render<PagerBar>(p => p.Add(x => x.Page, 2).Add(x => x.PageSize, 10).Add(x => x.TotalCount, 35).Add(x => x.Busy, true));
        Assert.All(busy.FindAll("button.ant-btn"), b => Assert.True(b.HasAttribute("disabled")));
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
        Assert.Empty(cut.FindAll("button.ant-btn")); // aturan bisnis: tidak ada "coba lagi"
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
        Assert.Contains("Muat ulang", concurrency.Find("button.ant-btn").TextContent);
        Assert.Contains("Coba lagi", server.Find("button.ant-btn").TextContent);
        concurrency.Find("button.ant-btn").Click();
        server.Find("button.ant-btn").Click();
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
    public void MegaMenu_menampilkan_Beranda_dan_kategori_dalam_nav_bernama_dan_panel_tertutup_awalnya()
    {
        var cut = Render<MegaMenu>(p => p.Add(x => x.Catalog, SampleCatalog()));

        Assert.Equal("Navigasi utama", cut.Find("nav").GetAttribute("aria-label"));
        Assert.Equal("Beranda", cut.Find("a.gms-mm-trigger").TextContent.Trim());
        Assert.Equal(["Penjualan", "Penggajian"], cut.FindAll("button.gms-mm-trigger").Select(b => b.TextContent.Trim()));
        Assert.All(cut.FindAll("button.gms-mm-trigger"), b => Assert.Equal("false", b.GetAttribute("aria-expanded")));
        Assert.Empty(cut.FindAll(".gms-mm-panel"));
    }

    [Fact]
    public void MegaMenu_dibuka_dengan_klik_satu_panel_saja_memisahkan_halaman_dan_aksi_dan_menutup_dengan_Esc_scrim_atau_klik_ulang()
    {
        var cut = Render<MegaMenu>(p => p.Add(x => x.Catalog, SampleCatalog()));

        cut.FindAll("button.gms-mm-trigger")[0].Click();

        Assert.Equal("true", cut.FindAll("button.gms-mm-trigger")[0].GetAttribute("aria-expanded"));
        var panel = cut.Find(".gms-mm-panel");
        Assert.Equal("region", panel.GetAttribute("role"));
        Assert.Equal(["Daftar PO"], panel.QuerySelectorAll("a.gms-mm-item .gms-mm-label").Select(x => x.TextContent.Trim()));
        Assert.Equal(["Buat PO"], panel.QuerySelectorAll("a.gms-mm-action").Select(x => x.TextContent.Trim()));
        Assert.Contains("Ctrl", cut.Find(".gms-mm-footer").TextContent);

        cut.FindAll("button.gms-mm-trigger")[1].Click(); // kategori lain menggantikan, bukan menumpuk
        Assert.Single(cut.FindAll(".gms-mm-panel"));
        Assert.Equal("Penggajian", cut.Find(".gms-mm-panel").GetAttribute("aria-label"));

        cut.Find(".gms-mm-panel").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".gms-mm-panel"));

        cut.FindAll("button.gms-mm-trigger")[0].Click();
        cut.Find(".gms-mm-scrim").Click();
        Assert.Empty(cut.FindAll(".gms-mm-panel"));

        cut.FindAll("button.gms-mm-trigger")[0].Click();
        cut.FindAll("button.gms-mm-trigger")[0].Click();
        Assert.Empty(cut.FindAll(".gms-mm-panel"));
    }

    [Fact]
    public void MegaMenu_menandai_kategori_dan_item_aktif_dengan_rute_terpanjang_dan_aria_current()
    {
        Nav.NavigateTo("/purchase-orders/new");
        var cut = Render<MegaMenu>(p => p.Add(x => x.Catalog, SampleCatalog()));

        Assert.Contains("active", cut.FindAll("button.gms-mm-trigger")[0].ClassName);
        Assert.DoesNotContain("active", cut.FindAll("button.gms-mm-trigger")[1].ClassName);

        cut.FindAll("button.gms-mm-trigger")[0].Click();
        Assert.Equal("page", cut.Find("a.gms-mm-action").GetAttribute("aria-current")); // /purchase-orders/new, bukan /purchase-orders
        Assert.Null(cut.Find("a.gms-mm-item").GetAttribute("aria-current"));
    }

    [Fact]
    public void MainLayout_memuat_skip_link_nama_pengguna_logout_berantiforgery_pengalih_tema_dan_katalog_palette_sesuai_izin()
    {
        SignIn(Permissions.CustomerPO.View);
        var cut = Render<MainLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id='isi'>Halaman</p>")));

        Assert.Equal("#gms-main", cut.Find("a.gms-skip").GetAttribute("href"));
        Assert.NotNull(cut.Find("main#gms-main #isi"));
        Assert.Contains("Budi", cut.Markup);
        Assert.Equal(["Penjualan"], cut.FindAll("button.gms-mm-trigger").Select(b => b.TextContent.Trim())); // hanya kategori yang boleh dilihat
        Assert.NotNull(cut.Find("button[data-palette-open]"));

        var catalog = cut.Find("#gms-catalog").GetAttribute("data-json")!;
        Assert.Contains("Daftar PO", catalog);
        Assert.DoesNotContain("Work Order", catalog); // tanpa izin tidak ada di palette
        Assert.DoesNotContain("Buat PO", catalog);    // aksi butuh izin Create

        Assert.Contains("Tema:", cut.Find("button.gms-iconbtn").GetAttribute("title"));
        cut.Find("button.gms-iconbtn").Click();
        Assert.Contains("terang", cut.Find("button.gms-iconbtn").GetAttribute("title"));

        cut.Find("button.gms-user-btn").Click();
        var form = cut.Find("form[action='/account/logout']");
        Assert.Equal("post", form.GetAttribute("method"));
        Assert.Contains("Ganti Password", cut.Markup);
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
