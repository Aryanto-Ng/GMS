using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Web.Components.Pages.Master;
using Gms.Web.Components.Pages.Master.FgParts;
using Gms.Web.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class TariffLogicTests
{
    [Theory]
    [InlineData("", null, null)]
    [InlineData("  ", null, null)]
    [InlineData("350", "350", null)]
    [InlineData("2.500", "2500", null)]
    [InlineData("350,5", "350.5", null)]
    [InlineData("0,12345", "0.12345", null)]
    [InlineData("Rp 1.250,25", "1250.25", null)]
    public void ParseCell_menerima_format_Indonesia_dan_kosong_berarti_belum_diisi(string text, string? expected, string? error)
    {
        var (rate, err) = TariffLogic.ParseCell(text);

        Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rate);
        Assert.Equal(error, err);
    }

    [Theory]
    [InlineData("0", "Harus lebih dari 0.")]
    [InlineData("-5", "Harus lebih dari 0.")]
    [InlineData("1,123456", "Maksimal 5 digit desimal.")]
    [InlineData("1.5", "Gunakan koma untuk desimal (mis. 1,5) dan titik hanya untuk ribuan (mis. 1.500).")]
    [InlineData("abc", "Hanya angka, titik ribuan, dan koma desimal yang diperbolehkan.")]
    [InlineData("1.000.000.000", "Terlalu besar.")]
    public void ParseCell_menolak_nilai_tidak_sah_tanpa_membulatkan_diam_diam(string text, string error)
    {
        var (rate, err) = TariffLogic.ParseCell(text);

        Assert.Null(rate);
        Assert.Equal(error, err);
    }

    [Fact]
    public void FormatCell_membuang_nol_ekor_dan_mengosongkan_null()
    {
        Assert.Equal("2.500", TariffLogic.FormatCell(2500m));
        Assert.Equal("350,5", TariffLogic.FormatCell(350.50000m));
        Assert.Equal(string.Empty, TariffLogic.FormatCell(null));
    }

    [Fact]
    public void FirstFilled_dan_WouldOverwrite_untuk_Isi_semua()
    {
        string?[] row = ["", "400", "300", null];

        Assert.Equal(400m, TariffLogic.FirstFilled(row));
        Assert.True(TariffLogic.WouldOverwrite(row, 400m)); // 300 akan ditimpa
        Assert.False(TariffLogic.WouldOverwrite(["", "400", "400,0"], 400m)); // nilai sama bukan timpa
        Assert.False(TariffLogic.WouldOverwrite(["", ""], 400m));
        Assert.Null(TariffLogic.FirstFilled(["", "abc"]));
    }
}

public sealed class MockFgTests
{
    private readonly MockSizeApi _sizes = new();
    private readonly MockProcessApi _processes = new();
    private readonly MockFgApi _fgs;

    public MockFgTests() => _fgs = new MockFgApi(_sizes, _processes, new MockCustomerApi());

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    [Fact]
    public async Task Daftar_menghitung_kesiapan_dan_filter_ReadyOnly_hanya_FG_siap()
    {
        var all = await _fgs.ListAsync(new FgListQuery());
        var ready = await _fgs.ListAsync(new FgListQuery { ReadyOnly = true });

        Assert.Equal([("KMJ-001", true), ("CLN-002", false)], all.Items.Select(i => (i.Code, i.IsReady)).OrderByDescending(x => x.Item2));
        Assert.Equal(["KMJ-001"], ready.Items.Select(i => i.Code));
    }

    [Fact]
    public async Task Kesiapan_menjelaskan_butir_gagal_dengan_target_perbaikan()
    {
        var r = await _fgs.GetReadinessAsync(MockFgApi.CelanaId);

        Assert.False(r.IsReady);
        Assert.Equal(["general", "sizes", "routing", "tariffs"], r.Items.Select(i => i.FixTarget));
        Assert.Equal([true, false, false, false], r.Items.Select(i => i.Ok));
        Assert.True((await _fgs.GetReadinessAsync(MockFgApi.KemejaId)).IsReady);
    }

    [Fact]
    public async Task Kode_terkunci_setelah_ada_struktur_dan_nama_tetap_dapat_diubah()
    {
        var kemeja = await _fgs.GetAsync(MockFgApi.KemejaId);
        Assert.True(kemeja.IsCodeLocked);

        var updated = await _fgs.UpdateAsync(kemeja.Id, new SaveFgRequest("UBAH", "Nama Baru", null, null, null, kemeja.RowVersion));

        Assert.Equal(("KMJ-001", "Nama Baru"), (updated.Code, updated.Name)); // kode tidak berubah
        Assert.False((await _fgs.GetAsync(MockFgApi.CelanaId)).IsCodeLocked);
    }

    [Fact]
    public async Task Hapus_size_bertarif_butuh_konfirmasi_dengan_rincian_lalu_menghapus_tarif_dan_membatalkan_kesiapan()
    {
        var fg = await _fgs.GetAsync(MockFgApi.KemejaId);
        var sizes = await _fgs.GetSizesAsync(fg.Id);
        var keep = sizes.SizeIds.Take(3).ToList();

        var needs = await Fails(() => _fgs.SaveSizesAsync(fg.Id, new SaveFgSizesRequest(keep, false, fg.RowVersion)));
        Assert.Equal(ProblemCodes.FgSizeHasTariffs, needs.Problem.Code);
        var violation = Assert.Single(needs.Problem.Violations!);
        Assert.Equal(("Size XL", 3m), (violation.Subject, violation.Current));

        var saved = await _fgs.SaveSizesAsync(fg.Id, new SaveFgSizesRequest(keep, true, fg.RowVersion));
        Assert.Equal((3, fg.RowVersion + 1), (saved.SizeIds.Count, saved.RowVersion));
        var grid = await _fgs.GetTariffsAsync(fg.Id);
        Assert.Equal(3, grid.Sizes.Count);
        Assert.DoesNotContain(grid.Cells, c => c.SizeId == sizes.SizeIds[3]);
        Assert.True((await _fgs.GetReadinessAsync(fg.Id)).IsReady); // 3 size × 3 proses tetap lengkap
    }

    [Fact]
    public async Task Size_kosong_size_tak_dikenal_dan_versi_basi_ditolak()
    {
        var fg = await _fgs.GetAsync(MockFgApi.KemejaId);

        Assert.Contains("sizeIds", (await Fails(() => _fgs.SaveSizesAsync(fg.Id, new SaveFgSizesRequest([], false, fg.RowVersion)))).Problem.Errors!.Keys);
        Assert.Contains("sizeIds", (await Fails(() => _fgs.SaveSizesAsync(fg.Id, new SaveFgSizesRequest([Guid.NewGuid()], false, fg.RowVersion)))).Problem.Errors!.Keys);
        var ids = (await _fgs.GetSizesAsync(fg.Id)).SizeIds;
        Assert.Equal(409, (await Fails(() => _fgs.SaveSizesAsync(fg.Id, new SaveFgSizesRequest(ids, false, fg.RowVersion + 7)))).StatusCode);
    }

    [Fact]
    public async Task Routing_menolak_kosong_dan_dobel_dan_langkah_bertarif_dihapus_dengan_konfirmasi()
    {
        var fg = await _fgs.GetAsync(MockFgApi.KemejaId);
        var order = (await _fgs.GetRoutingAsync(fg.Id)).Steps.Select(s => s.ProcessId).ToList();

        Assert.Contains("processIds", (await Fails(() => _fgs.SaveRoutingAsync(fg.Id, new SaveRoutingRequest([], false, fg.RowVersion)))).Problem.Errors!.Keys);
        Assert.Contains("processIds", (await Fails(() => _fgs.SaveRoutingAsync(fg.Id, new SaveRoutingRequest([order[0], order[0]], false, fg.RowVersion)))).Problem.Errors!.Keys);

        var without = order.Where(p => p != MockProcessApi.SewingId).ToList();
        var needs = await Fails(() => _fgs.SaveRoutingAsync(fg.Id, new SaveRoutingRequest(without, false, fg.RowVersion)));
        Assert.Equal(ProblemCodes.FgStepHasTariffs, needs.Problem.Code);
        Assert.Equal(4m, Assert.Single(needs.Problem.Violations!).Current);

        var saved = await _fgs.SaveRoutingAsync(fg.Id, new SaveRoutingRequest(without, true, fg.RowVersion));
        Assert.Equal([1, 2, 3], saved.Steps.Select(s => s.Seq));
        Assert.Equal(2, (await _fgs.GetTariffsAsync(fg.Id)).InternalSteps.Count);
    }

    [Fact]
    public async Task Mengurutkan_ulang_routing_tidak_membutuhkan_konfirmasi_dan_mempertahankan_tarif()
    {
        var fg = await _fgs.GetAsync(MockFgApi.KemejaId);
        var order = (await _fgs.GetRoutingAsync(fg.Id)).Steps.Select(s => s.ProcessId).Reverse().ToList();

        var saved = await _fgs.SaveRoutingAsync(fg.Id, new SaveRoutingRequest(order, false, fg.RowVersion));

        Assert.Equal(order, saved.Steps.Select(s => s.ProcessId));
        Assert.Equal(12, (await _fgs.GetTariffsAsync(fg.Id)).Cells.Count(c => c.Rate is not null));
    }

    [Fact]
    public async Task Grid_tarif_memisahkan_langkah_internal_dan_vendor_dan_menolak_sel_tidak_sah()
    {
        var fg = await _fgs.GetAsync(MockFgApi.KemejaId);
        var grid = await _fgs.GetTariffsAsync(fg.Id);
        Assert.Equal(["CUT", "SEW", "QC"], grid.InternalSteps.Select(s => s.ProcessCode));
        Assert.Equal(["BRD"], grid.VendorSteps.Select(s => s.ProcessCode));
        Assert.Equal(12, grid.Cells.Count);

        var size = grid.Sizes[0].Id;
        var request = new SaveTariffsRequest(
            [new TariffCell(grid.InternalSteps[0].ProcessId, size, 0m), new TariffCell(grid.InternalSteps[1].ProcessId, size, 1.123456m),
             new TariffCell(grid.VendorSteps[0].ProcessId, size, 5m), new TariffCell(grid.InternalSteps[2].ProcessId, Guid.NewGuid(), 5m)], grid.RowVersion);
        var bad = await Fails(() => _fgs.SaveTariffsAsync(fg.Id, request));

        Assert.Equal(["cells[0].rate", "cells[1].rate", "cells[2].processId", "cells[3].sizeId"], bad.Problem.Errors!.Keys.Order());
        Assert.Equal("Tarif harus lebih dari 0.", bad.Problem.Errors["cells[0].rate"][0]);
        Assert.Equal("Tarif maksimal 5 digit di belakang koma.", bad.Problem.Errors["cells[1].rate"][0]);
    }

    [Fact]
    public async Task Menyimpan_tarif_menaikkan_versi_dan_mengosongkan_sel_membatalkan_kesiapan()
    {
        var fg = await _fgs.GetAsync(MockFgApi.KemejaId);
        var grid = await _fgs.GetTariffsAsync(fg.Id);

        var saved = await _fgs.SaveTariffsAsync(fg.Id, new SaveTariffsRequest([new TariffCell(grid.InternalSteps[1].ProcessId, grid.Sizes[3].Id, null)], grid.RowVersion));

        Assert.Equal(grid.RowVersion + 1, saved.RowVersion);
        Assert.Contains(saved.Cells, c => c.Rate is null);
        var readiness = await _fgs.GetReadinessAsync(fg.Id);
        Assert.False(readiness.IsReady);
        Assert.Equal("Tarif Sewing untuk size XL belum diisi.", Assert.Single(readiness.Items, i => i is { Code: "fg.tariffs", Ok: false }).Message);
    }

    [Fact]
    public async Task Salin_dari_FG_lain_pratinjau_melewati_size_tidak_berlaku_dan_menimpa_hanya_dengan_konfirmasi()
    {
        var celana = await _fgs.GetAsync(MockFgApi.CelanaId);
        var sizes = await _sizes.ListAsync(new MasterListQuery());
        var smallAndMedium = sizes.Items.Take(2).Select(s => s.Id).ToList();
        await _fgs.SaveSizesAsync(celana.Id, new SaveFgSizesRequest(smallAndMedium, false, celana.RowVersion));

        var preview = await _fgs.PreviewCopyFromAsync(celana.Id, new CopyFgRequest(MockFgApi.KemejaId));
        Assert.Equal(4, preview.Steps.Count);
        Assert.Equal(6, preview.Cells.Count); // 3 proses × 2 size
        Assert.Equal(["L", "XL"], preview.SkippedSizeCodes);
        Assert.False(preview.WillOverwriteExisting);

        var copied = await _fgs.CopyFromAsync(celana.Id, new CopyFgRequest(MockFgApi.KemejaId));
        Assert.True(copied.IsReady);
        var again = await Fails(() => _fgs.CopyFromAsync(celana.Id, new CopyFgRequest(MockFgApi.KemejaId)));
        Assert.Equal(ProblemCodes.FgCopyNeedsConfirm, again.Problem.Code);
        Assert.True((await _fgs.CopyFromAsync(celana.Id, new CopyFgRequest(MockFgApi.KemejaId, true))).IsReady);
    }

    [Fact]
    public async Task Salin_menolak_sumber_sama_tak_dikenal_dan_tanpa_routing()
    {
        Assert.Contains("sourceFgId", (await Fails(() => _fgs.PreviewCopyFromAsync(MockFgApi.KemejaId, new CopyFgRequest(MockFgApi.KemejaId)))).Problem.Errors!.Keys);
        Assert.Contains("sourceFgId", (await Fails(() => _fgs.PreviewCopyFromAsync(MockFgApi.KemejaId, new CopyFgRequest(Guid.NewGuid())))).Problem.Errors!.Keys);
        Assert.Contains("belum memiliki routing", (await Fails(() => _fgs.PreviewCopyFromAsync(MockFgApi.KemejaId, new CopyFgRequest(MockFgApi.CelanaId)))).Problem.Errors!["sourceFgId"][0]);
    }
}

public sealed class FgPageTests : WebTestBase
{
    private static readonly string[] AllMaster = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("MasterData.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly MockFgApi _fgs;

    public FgPageTests()
    {
        var sizes = new MockSizeApi();
        var processes = new MockProcessApi();
        var customers = new MockCustomerApi();
        _fgs = new MockFgApi(sizes, processes, customers);
        Services.AddSingleton<IFgApi>(_fgs);
        Services.AddSingleton<ISizeApi>(sizes);
        Services.AddSingleton<IProcessApi>(processes);
        Services.AddSingleton<ICustomerApi>(customers);
        SignIn(AllMaster);
    }

    private static T Sync<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private IRenderedComponent<FgDetailPage> Detail(Guid? id) => Render<FgDetailPage>(p => p.Add(x => x.Id, id));

    private static IElement Tab(IRenderedComponent<FgDetailPage> cut, string label) => cut.FindAll("[role=tab]").Single(t => t.TextContent == label);

    private static IElement LastDialogButton(IRenderedComponent<FgDetailPage> cut) => cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelectorAll("fluent-button").Last();

    private static void WaitReady(IRenderedComponent<FgDetailPage> cut) => cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=tab]")));

    // ===== Daftar =====

    [Fact]
    public void Daftar_menampilkan_chip_kesiapan_dan_filter_Hanya_Siap_Produksi()
    {
        var cut = Render<FgListPage>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Contains("Siap Produksi", cut.Find("tbody").TextContent);
        Assert.Contains("Belum Siap", cut.Find("tbody").TextContent);
        cut.Find("#fg-ready").Change(true);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains("KMJ-001", cut.Find("tbody").TextContent);
    }

    [Fact]
    public void Tambah_FG_menavigasi_ke_form_baru_dan_baris_menautkan_ke_detail()
    {
        var cut = Render<FgListPage>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));

        Assert.Contains(cut.FindAll("tbody a"), a => a.GetAttribute("href")!.EndsWith($"/master/fgs/{MockFgApi.KemejaId}"));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Tambah FG")).Click();

        Assert.EndsWith("/master/fgs/new", Nav.Uri);
    }

    // ===== Detail =====

    [Fact]
    public void Detail_menampilkan_tab_sesuai_izin_dan_tab_Tarif_disembunyikan_tanpa_Tarif_View()
    {
        var withTariff = Detail(MockFgApi.KemejaId);
        WaitReady(withTariff);
        Assert.Equal(["Umum", "Size Berlaku", "Routing", "Tarif", "Kesiapan"], withTariff.FindAll("[role=tab]").Select(t => t.TextContent));
    }

    [Fact]
    public void Tab_Tarif_disembunyikan_tanpa_izin_Tarif_View()
    {
        SignIn(Permissions.MasterData.FG.View);
        var without = Detail(MockFgApi.KemejaId);
        WaitReady(without);
        Assert.Equal(["Umum", "Size Berlaku", "Routing", "Kesiapan"], without.FindAll("[role=tab]").Select(t => t.TextContent));
    }

    [Fact]
    public void Tab_aksesibel_dengan_aria_selected_dan_panel_terhubung_serta_chip_status_di_header()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);

        Assert.Equal("true", Tab(cut, "Umum").GetAttribute("aria-selected"));
        Assert.Equal("false", Tab(cut, "Routing").GetAttribute("aria-selected"));
        Assert.Equal("panel-General", Tab(cut, "Umum").GetAttribute("aria-controls"));
        Assert.Equal("tab-General", cut.Find("[role=tabpanel]").GetAttribute("aria-labelledby"));
        Assert.Contains("Siap Produksi", cut.Find(".gms-pageheader").TextContent);
        Assert.Contains("KMJ-001 · Kemeja Wearpak Lengan Panjang", cut.Find("h1").TextContent);
    }

    [Fact]
    public void FG_tidak_ditemukan_menampilkan_state_galat_tanpa_tombol_coba_lagi()
    {
        var cut = Detail(Guid.NewGuid());

        cut.WaitForAssertion(() => Assert.Contains("Data tidak ditemukan", cut.Markup));
        Assert.Empty(cut.FindAll("[role=tab]"));
    }

    [Fact]
    public void Tab_Umum_mengunci_kode_bila_ada_struktur_dan_menyimpan_perubahan_nama()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);

        var inputs = cut.FindAll("form input");
        Assert.True(inputs[0].HasAttribute("disabled"));
        Assert.Contains("Terkunci", cut.Find("form").TextContent);
        inputs[1].Input("Kemeja Baru");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("KMJ-001 · Kemeja Baru", cut.Find("h1").TextContent));
    }

    [Fact]
    public void Tambah_FG_baru_hanya_menampilkan_tab_Umum_dan_menavigasi_setelah_simpan()
    {
        var cut = Detail(null);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));

        Assert.Equal(["Umum"], cut.FindAll("[role=tab]").Select(t => t.TextContent));
        var inputs = cut.FindAll("form input");
        inputs[0].Input("JKT-009");
        inputs[1].Input("Jaket");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Matches("/master/fgs/[0-9a-f-]{36}$", Nav.Uri));
    }

    [Fact]
    public void Size_Berlaku_mengubah_pilihan_memerlukan_konfirmasi_bila_bertarif()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Size Berlaku").Click();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".gms-checklist input").Count));

        cut.FindAll(".gms-checklist input").Last().Change(false); // XL
        cut.FindAll("fieldset fluent-button").Single(b => b.TextContent.Contains("Simpan Size")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Hapus size bertarif?", cut.Markup));
        Assert.Contains("Tarif size XL (3 proses) akan dihapus.", cut.Markup);
        LastDialogButton(cut).Click();
        cut.WaitForAssertion(() => Assert.Equal(3, Sync(_fgs.GetSizesAsync(MockFgApi.KemejaId)).SizeIds.Count));
    }

    [Fact]
    public void Size_Berlaku_tanpa_pilihan_menampilkan_peringatan_dan_menahan_simpan()
    {
        var cut = Detail(MockFgApi.CelanaId);
        WaitReady(cut);
        Tab(cut, "Size Berlaku").Click();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".gms-checklist input").Count));

        Assert.Contains("Minimal satu Size Berlaku", cut.Find("fieldset").TextContent);
        cut.FindAll(".gms-checklist input")[0].Change(true);
        Assert.False(cut.FindAll("fieldset fluent-button").Single(b => b.TextContent.Contains("Simpan Size")).HasAttribute("disabled"));
    }

    [Fact]
    public void Routing_menampilkan_langkah_berurutan_dengan_tombol_keyboard_dan_menukar_urutan()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Routing").Click();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".gms-steps li").Count));

        Assert.Equal(["Cutting", "Bordir", "Sewing", "QC"], cut.FindAll(".gms-step-name").Select(e => e.FirstChild!.TextContent.Trim()));
        Assert.True(cut.Find("[aria-label='Naikkan Cutting']").HasAttribute("disabled"));
        Assert.True(cut.Find("[aria-label='Turunkan QC']").HasAttribute("disabled"));
        cut.Find("[aria-label='Turunkan Cutting']").Click();

        Assert.Equal(["Bordir", "Cutting", "Sewing", "QC"], cut.FindAll(".gms-step-name").Select(e => e.FirstChild!.TextContent.Trim()));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Simpan routing")).Click();
        cut.WaitForAssertion(() => Assert.Equal(MockProcessApi.BordirId, Sync(_fgs.GetRoutingAsync(MockFgApi.KemejaId)).Steps[0].ProcessId));
    }

    [Fact]
    public void Routing_pilihan_tambah_hanya_proses_aktif_yang_belum_ada_dan_hapus_langkah_bertarif_meminta_konfirmasi()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Routing").Click();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".gms-steps li").Count));

        Assert.Single(cut.FindAll("#add-process option")); // semua proses sudah ada: hanya placeholder
        cut.Find("[aria-label='Hapus langkah Sewing']").Click();
        Assert.Contains("Cutting", cut.Find("#add-process").TextContent.Replace("Pilih proses…", "").Length > 0 ? "Cutting" : "");
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Simpan routing")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Hapus langkah bertarif?", cut.Markup));
        Assert.Contains("Tarif langkah Sewing (4 size) akan dihapus.", cut.Markup);
    }

    [Fact]
    public void Tarif_menampilkan_grid_dengan_label_sel_dan_baris_vendor_tanpa_tarif()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Tarif").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-tariff")));

        Assert.Equal("2.500", cut.Find("input[aria-label='Sewing, size S, tarif']").GetAttribute("value"));
        Assert.Equal("350", cut.Find("input[aria-label='Cutting, size XL, tarif']").GetAttribute("value"));
        Assert.Contains("Tanpa tarif (dikerjakan vendor)", cut.Find(".gms-vendorrow").TextContent);
        Assert.Equal(12, cut.FindAll(".gms-tariff input").Count);
        Assert.DoesNotContain("belum diisi", cut.Markup.Replace("Tanpa tarif", ""));
    }

    [Fact]
    public void Tarif_input_tidak_sah_menampilkan_galat_sel_terhubung_dan_tombol_simpan_tidak_mengirim()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Tarif").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-tariff")));

        var cell = cut.Find("input[aria-label='Sewing, size S, tarif']");
        cell.Change("1,123456");
        cell = cut.Find("input[aria-label='Sewing, size S, tarif']");
        Assert.Equal("true", cell.GetAttribute("aria-invalid"));
        Assert.Contains("Maksimal 5 digit desimal.", cut.Find($"#{cell.GetAttribute("aria-describedby")}").TextContent);
        cut.Find("input[aria-label='Sewing, size M, tarif']").Change("1.5");
        Assert.Contains("Gunakan koma untuk desimal", cut.Markup);
        var versionBefore = Sync(_fgs.GetAsync(MockFgApi.KemejaId)).RowVersion;
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Simpan tarif")).Click();

        Assert.Equal(versionBefore, Sync(_fgs.GetAsync(MockFgApi.KemejaId)).RowVersion);
    }

    [Fact]
    public void Tarif_ubah_sel_menandai_perubahan_lalu_simpan_menyimpan_ke_server_dan_mengosongkan_sel_membuat_peringatan()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Tarif").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-tariff")));

        Assert.DoesNotContain("Ada perubahan belum disimpan", cut.Markup);
        cut.Find("input[aria-label='QC, size L, tarif']").Change("450,5");
        Assert.Contains("Ada perubahan belum disimpan", cut.Markup);
        cut.Find("input[aria-label='Sewing, size XL, tarif']").Change("");
        Assert.Contains("1 sel belum diisi (Sewing · XL)", cut.Find(".gms-warn").TextContent);
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Simpan tarif")).Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Ada perubahan belum disimpan", cut.Markup));
        var grid = Sync(_fgs.GetTariffsAsync(MockFgApi.KemejaId));
        Assert.Contains(grid.Cells, c => c.Rate == 450.5m);
        Assert.Contains(grid.Cells, c => c.Rate is null);
    }

    [Fact]
    public void Tarif_Isi_semua_mengisi_baris_dan_meminta_konfirmasi_bila_menimpa_isian_berbeda()
    {
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Tarif").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-tariff")));
        cut.Find("input[aria-label='QC, size M, tarif']").Change("");
        cut.Find("input[aria-label='QC, size S, tarif']").Change("");

        cut.Find("[aria-label='Isi semua QC']").Click(); // yang terisi seragam (400) → langsung mengisi tanpa konfirmasi
        Assert.Equal("400", cut.Find("input[aria-label='QC, size M, tarif']").GetAttribute("value"));

        cut.Find("input[aria-label='QC, size XL, tarif']").Change("999");
        cut.Find("[aria-label='Isi semua QC']").Click(); // nilai pertama 400 akan menimpa 999 → konfirmasi
        cut.WaitForAssertion(() => Assert.Contains("Timpa isian baris ini?", cut.Markup));
        LastDialogButton(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal("400", cut.Find("input[aria-label='QC, size XL, tarif']").GetAttribute("value")));
    }

    [Fact]
    public void Tarif_hanya_baca_tanpa_izin_Edit_nonaktifkan_input_dan_sembunyikan_simpan()
    {
        SignIn(Permissions.MasterData.FG.View, Permissions.MasterData.Tarif.View, Permissions.MasterData.Routing.View);
        var cut = Detail(MockFgApi.KemejaId);
        WaitReady(cut);
        Tab(cut, "Tarif").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-tariff")));

        Assert.All(cut.FindAll(".gms-tariff input"), i => Assert.True(i.HasAttribute("disabled")));
        Assert.DoesNotContain("Simpan tarif", cut.Markup);
        Assert.DoesNotContain("Isi semua", cut.Find(".gms-tariff").TextContent);
    }

    [Fact]
    public void Tarif_kosong_bila_belum_ada_routing_menawarkan_pindah_ke_tab_terkait()
    {
        var cut = Detail(MockFgApi.CelanaId);
        WaitReady(cut);
        Tab(cut, "Tarif").Click();

        cut.WaitForAssertion(() => Assert.Contains("Tarif muncul setelah routing dan Size Berlaku diisi", cut.Markup));
        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Ke Routing")).Click();

        Assert.Equal("true", Tab(cut, "Routing").GetAttribute("aria-selected"));
    }

    [Fact]
    public void Kesiapan_FG_belum_siap_menjelaskan_butir_gagal_dibacakan_dan_tautan_perbaikan_membuka_tab()
    {
        var cut = Detail(MockFgApi.CelanaId);
        WaitReady(cut);
        Tab(cut, "Kesiapan").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-readiness li")));
        var items = cut.FindAll(".gms-readiness li");
        Assert.Contains("Terpenuhi:", items[0].TextContent);
        Assert.Contains("Belum terpenuhi:", items[1].TextContent);
        Assert.Contains("Belum ada Size Berlaku.", items[1].TextContent);
        items[2].QuerySelector("fluent-button")!.Click();

        Assert.Equal("true", Tab(cut, "Routing").GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task Salin_dari_FG_lain_wizard_tiga_langkah_menyalin_routing_dan_tarif()
    {
        var celana = await _fgs.GetAsync(MockFgApi.CelanaId);
        var sizeIds = (await _fgs.GetSizesAsync(MockFgApi.KemejaId)).SizeIds;
        await _fgs.SaveSizesAsync(celana.Id, new SaveFgSizesRequest(sizeIds, false, celana.RowVersion));
        var cut = Detail(MockFgApi.CelanaId);
        WaitReady(cut);

        cut.FindAll("fluent-button").Single(b => b.TextContent.Contains("Salin dari FG lain")).Click();
        Assert.Equal("step", cut.Find("[aria-current=step]").GetAttribute("aria-current"));
        cut.WaitForAssertion(() => Assert.Contains("KMJ-001", cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent));
        cut.FindAll("dialog select").Last().Change(MockFgApi.KemejaId.ToString());
        cut.FindAll("dialog fluent-button").Single(b => b.TextContent.Contains("Lanjut")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Routing yang akan disalin", cut.Markup));
        Assert.Contains("12 sel tarif akan disalin.", cut.Markup);
        cut.FindAll("dialog fluent-button").Single(b => b.TextContent.Contains("Lanjut")).Click();
        cut.WaitForAssertion(() => Assert.Contains("belum memiliki routing/tarif", cut.Markup));
        LastDialogButton(cut).Click();

        cut.WaitForAssertion(() => Assert.True(Sync(_fgs.GetReadinessAsync(MockFgApi.CelanaId)).IsReady));
    }
}
