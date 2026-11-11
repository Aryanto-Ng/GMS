using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Gms.Web.Components.Pages.Inventory;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class MockInventoryTests
{
    private readonly ProductionRig _rig = new();

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    /// <summary>Output Cutting → vendor → Sewing → QC pada baris 0 (qty) sehingga langkah terakhir memiliki output.</summary>
    private async Task ProduceAsync(int qty)
    {
        await _rig.Output(1, qty);
        var date = new DateOnly(2026, 11, 9);
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, date, null, null, [new WoLineQty(_rig.Line(0), qty)]));
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Receive, date, null, null, [new WoLineQty(_rig.Line(0), qty)]));
        await _rig.Output(3, qty);
        await _rig.Output(4, qty);
    }

    private SaveHandoverFgRequest Handover(int qty, DateOnly? date = null) =>
        new(ProductionRig.WoId, date ?? new DateOnly(2026, 11, 9), "ST-1", "Gudang", null, [new WoLineQty(_rig.Line(0), qty)]);

    [Fact]
    public async Task Konteks_menunjukkan_batas_serah_dan_memblokir_WO_yang_belum_berjalan()
    {
        var released = await _rig.Inventory.GetHandoverFgContextAsync(ProductionRig.WoId);
        Assert.Contains("Released", released.BlockedReason);

        await ProduceAsync(30);
        var ctx = await _rig.Inventory.GetHandoverFgContextAsync(ProductionRig.WoId);
        Assert.Null(ctx.BlockedReason);
        Assert.Equal((30, 0, 30, 50), (ctx.Columns[0].MaxHandover, ctx.Columns[0].HandedOver, ctx.Columns[0].LastStepOutput, ctx.Columns[0].WoQty));
    }

    [Fact]
    public async Task Serah_terima_menambah_stok_mengalokasikan_otomatis_ke_PO_dan_mengurangi_Dalam_Produksi()
    {
        await ProduceAsync(30);
        var before = await _rig.Pos.GetAsync(MockPoApi.SeedPoId);

        var preview = await _rig.Inventory.PreviewHandoverFgAsync(Handover(30));
        Assert.Equal((30, 0, "PO-202611-0001"), (preview.AutoAllocatedQty, preview.FreeStockQty, preview.PoCode));
        var result = await _rig.Inventory.CreateHandoverFgAsync(Handover(30));

        Assert.Equal((30, 0), (result.AutoAllocatedQty, result.FreeStockQty));
        var after = await _rig.Pos.GetAsync(MockPoApi.SeedPoId);
        Assert.Equal((before.AllocatedQty + 30, before.InProductionQty - 30), (after.AllocatedQty, after.InProductionQty));
        var stock = (await _rig.Inventory.ListStockAsync(new StockQuery())).Rows.Items.Single(r => r.ColorSize.SizeCode == "S");
        Assert.Equal((150, 30), (stock.OnHand, stock.Allocated)); // 120 saldo awal + 30
        Assert.Equal(30, (await _rig.Wos.GetAsync(ProductionRig.WoId)).HandedOverQty);
    }

    [Fact]
    public async Task Kelebihan_produksi_menjadi_stok_bebas_dan_batas_serah_dijaga_server()
    {
        await _rig.Output(1, 60, confirm: true); // langkah pertama > Qty WO (50) perlu konfirmasi
        var date = new DateOnly(2026, 11, 9);
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, date, null, null, [new WoLineQty(_rig.Line(0), 60)]));
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Receive, date, null, null, [new WoLineQty(_rig.Line(0), 60)]));
        await _rig.Output(3, 60);
        await _rig.Output(4, 60);

        var tooMany = await Fails(() => _rig.Inventory.CreateHandoverFgAsync(Handover(61)));
        Assert.Equal(ProblemCodes.HandoverExceedsOutput, tooMany.Problem.Code);
        Assert.Equal((61m, 60m), (tooMany.Problem.Violations![0].Current, tooMany.Problem.Violations[0].Limit));

        var result = await _rig.Inventory.CreateHandoverFgAsync(Handover(60));
        Assert.Equal((50, 10), (result.AutoAllocatedQty, result.FreeStockQty)); // 50 sesuai Qty WO, 10 stok bebas
    }

    [Fact]
    public async Task Void_serah_terima_dengan_pratinjau_melepas_alokasi_otomatis_dan_mengembalikan_Dalam_Produksi()
    {
        await ProduceAsync(30);
        var result = await _rig.Inventory.CreateHandoverFgAsync(Handover(30));

        var preview = await _rig.Inventory.PreviewVoidHandoverFgAsync(result.Id);
        Assert.True(preview.CanProceed);
        Assert.Contains("30 pcs", preview.Notes[0]);

        await _rig.Inventory.VoidHandoverFgAsync(result.Id, new ReasonRequest("salah"));
        Assert.Equal(0, (await _rig.Wos.GetAsync(ProductionRig.WoId)).HandedOverQty);
        Assert.Equal(DocStatus.Voided, (await _rig.Inventory.GetHandoverFgAsync(result.Id)).Item.Status);
        Assert.False((await _rig.Inventory.GetHandoverFgAsync(result.Id)).CanVoid);
    }

    [Fact]
    public async Task Koreksi_membuat_dokumen_pengganti_dengan_tautan_dan_alasan_wajib()
    {
        await ProduceAsync(30);
        var result = await _rig.Inventory.CreateHandoverFgAsync(Handover(20));
        var request = new CorrectHandoverFgRequest("salah jumlah", new DateOnly(2026, 11, 9), "ST-1", "Gudang", null, [new WoLineQty(_rig.Line(0), 25)]);

        Assert.Contains("reason", (await Fails(() => _rig.Inventory.CorrectHandoverFgAsync(result.Id, request with { Reason = " " }))).Problem.Errors!.Keys);
        Assert.True((await _rig.Inventory.PreviewCorrectHandoverFgAsync(result.Id, request)).CanProceed);
        var corrected = await _rig.Inventory.CorrectHandoverFgAsync(result.Id, request);

        var old = await _rig.Inventory.GetHandoverFgAsync(result.Id);
        var neu = await _rig.Inventory.GetHandoverFgAsync(corrected.Id);
        Assert.Equal((DocStatus.Voided, corrected.Id), (old.Item.Status, old.ReplacedById));
        Assert.Equal((result.Id, 25), (neu.ReplacesId, neu.Item.TotalQty));
        Assert.Equal(25, (await _rig.Wos.GetAsync(ProductionRig.WoId)).HandedOverQty);
    }

    [Fact]
    public async Task Kartu_stok_saldo_berjalan_urut_terbaru_di_atas_dan_penyesuaian_tidak_boleh_membuat_stok_negatif_atau_tersedia_negatif()
    {
        var s = (await _rig.Inventory.ListStockAsync(new StockQuery())).Rows.Items.First();
        var req = new AdjustStockRequest(s.FgId, s.ColorSize.ColorId, s.ColorSize.SizeId, -10, new DateOnly(2026, 11, 9), "rusak");

        Assert.True((await _rig.Inventory.PreviewAdjustStockAsync(req)).CanProceed);
        await _rig.Inventory.AdjustStockAsync(req);
        var ledger = await _rig.Inventory.GetStockLedgerAsync(new StockLedgerQuery { FgId = s.FgId, ColorId = s.ColorSize.ColorId, SizeId = s.ColorSize.SizeId });
        Assert.Equal([110, 120], ledger.Rows.Items.Select(r => r.RunningBalance));
        Assert.Equal([StockLedgerKind.Adjustment, StockLedgerKind.OpeningBalance], ledger.Rows.Items.Select(r => r.Kind));
        Assert.Equal(110, ledger.Header.OnHand);

        var tooMuch = await _rig.Inventory.PreviewAdjustStockAsync(req with { Qty = -200 });
        Assert.False(tooMuch.CanProceed);
        Assert.Contains("negatif", tooMuch.Rows[0].Message);
        Assert.Equal("stock.insufficient", (await Fails(() => _rig.Inventory.AdjustStockAsync(req with { Qty = -200 }))).Problem.Code);
        Assert.Contains("qty", (await Fails(() => _rig.Inventory.AdjustStockAsync(req with { Qty = 0 }))).Problem.Errors!.Keys);
        Assert.Contains("reason", (await Fails(() => _rig.Inventory.AdjustStockAsync(req with { Reason = "" }))).Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Tersedia_negatif_karena_alokasi_menahan_penyesuaian_dengan_pesan_lepas_alokasi()
    {
        var line = (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        await _rig.Pos.AllocateAsync(line.Id, new AllocateRequest(50, null));

        var preview = await _rig.Inventory.PreviewAdjustStockAsync(new AdjustStockRequest(line.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId, -100, new DateOnly(2026, 11, 9), "x"));

        Assert.False(preview.CanProceed);
        Assert.Contains("Lepas alokasi", preview.Rows[0].Message);
    }

    [Fact]
    public async Task Daftar_stok_menyaring_dan_meringkas_serta_hanya_yang_ada_stok_default()
    {
        var all = await _rig.Inventory.ListStockAsync(new StockQuery());
        Assert.Equal((480, 0, 480), (all.OnHandTotal, all.AllocatedTotal, all.AvailableTotal)); // 4 size × 120
        Assert.Equal(4, all.Rows.TotalCount);
        Assert.Equal(1, (await _rig.Inventory.ListStockAsync(new StockQuery { SizeId = all.Rows.Items[0].ColorSize.SizeId })).Rows.TotalCount);
        Assert.Equal(0, (await _rig.Inventory.ListStockAsync(new StockQuery { Q = "tidak-ada" })).Rows.TotalCount);
    }

    [Fact]
    public async Task Serah_terima_material_memvalidasi_dan_dapat_di_void()
    {
        var material = (await _rig.Materials.LookupAsync(null))[0];
        SaveMaterialHandoverRequest Req(decimal qty) => new(ProductionRig.WoId, new DateOnly(2026, 11, 9), "SM-1", null, [new MaterialHandoverLine(material.Id, qty, null)]);

        Assert.Contains("lines[0].qty", (await Fails(() => _rig.Inventory.CreateMaterialHandoverAsync(Req(0)))).Problem.Errors!.Keys);
        Assert.Contains("3 digit", (await Fails(() => _rig.Inventory.CreateMaterialHandoverAsync(Req(1.2345m)))).Problem.Errors!["lines[0].qty"][0]);
        var id = await _rig.Inventory.CreateMaterialHandoverAsync(Req(120.5m));

        var doc = await _rig.Inventory.GetMaterialHandoverAsync(id);
        Assert.Equal((1, 120.5m, "BN-001"), (doc.Lines.Count, doc.Lines[0].Qty, doc.Lines[0].MaterialCode));
        await _rig.Inventory.VoidMaterialHandoverAsync(id, new ReasonRequest("salah"));
        Assert.Equal(DocStatus.Voided, (await _rig.Inventory.GetMaterialHandoverAsync(id)).Item.Status);
        Assert.Equal(ProblemTypes.StateConflict, (await Fails(() => _rig.Inventory.VoidMaterialHandoverAsync(id, new ReasonRequest("lagi")))).Problem.Type);
    }
}

public sealed class InventoryPageTests : WebTestBase
{
    private static readonly string[] AllInv = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("SerahTerima", StringComparison.Ordinal) || p.Key.StartsWith("StokFG.", StringComparison.Ordinal) || p.Key.StartsWith("WorkOrder.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly ProductionRig _rig = new();

    public InventoryPageTests()
    {
        _rig.Inventory.Today = _rig.Production.Today = () => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Services.AddSingleton<IInventoryApi>(_rig.Inventory);
        Services.AddSingleton<IWorkOrdersApi>(_rig.Wos);
        Services.AddSingleton<IFgApi>(_rig.Fgs);
        Services.AddSingleton<IColorApi>(_rig.Colors);
        Services.AddSingleton<ISizeApi>(_rig.Sizes);
        Services.AddSingleton<IMaterialApi>(_rig.Materials);
        SignIn(AllInv);
    }

    private async Task ProduceAsync(int qty)
    {
        await _rig.Output(1, qty);
        var date = new DateOnly(2026, 11, 9);
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, date, null, null, [new WoLineQty(_rig.Line(0), qty)]));
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Receive, date, null, null, [new WoLineQty(_rig.Line(0), qty)]));
        await _rig.Output(3, qty);
        await _rig.Output(4, qty);
    }

    private static IElement Dialog<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null);

    private static IElement DialogConfirm<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => Dialog(cut).QuerySelectorAll("fluent-button, button.ant-btn").Last();

    [Fact]
    public async Task Form_Serah_Terima_menampilkan_batas_perkiraan_alokasi_dan_menyimpan_lalu_menavigasi_ke_detail()
    {
        await ProduceAsync(30);
        var cut = Render<HandoverFgFormPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));

        Assert.Contains("maks 30", cut.Find("table.gms-matrix").TextContent);
        cut.Find("table.gms-matrix input[aria-label='Qty, Size Navy S']").Input("30");
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent == "Perkiraan alokasi").Click();
        cut.WaitForAssertion(() => Assert.Contains("Perkiraan alokasi otomatis ke PO-202611-0001", cut.Markup));
        Assert.Contains("Stok bebas (kelebihan produksi): 0 pcs", cut.FindAll("p[role=status]").Single(p => p.TextContent.Contains("Perkiraan")).TextContent);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Matches("/handovers/fg/[0-9a-f-]{36}$", Nav.Uri));
    }

    [Fact]
    public async Task Form_Serah_Terima_melebihi_batas_ditandai_pada_sel_dan_WO_Released_diblokir()
    {
        await ProduceAsync(30);
        var cut = Render<HandoverFgFormPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));

        cut.Find("table.gms-matrix input[aria-label='Qty, Size Navy S']").Input("31");

        Assert.Equal("true", cut.Find("table.gms-matrix input[aria-label='Qty, Size Navy S']").GetAttribute("aria-invalid"));
        Assert.Contains("melebihi batas", cut.Find(".gms-cellmsg").TextContent);
    }

    [Fact]
    public async Task Daftar_dan_detail_Serah_Terima_menampilkan_alokasi_otomatis_dan_Void_dengan_pratinjau()
    {
        await ProduceAsync(30);
        var result = await _rig.Inventory.CreateHandoverFgAsync(new SaveHandoverFgRequest(ProductionRig.WoId, new DateOnly(2026, 11, 9), "ST-9", "Gudang", null, [new WoLineQty(_rig.Line(0), 30)]));

        var list = Render<HandoverFgListPage>();
        list.WaitForAssertion(() => Assert.Single(list.FindAll("tbody tr")));
        Assert.Contains("ST-9", list.Find("tbody").TextContent);

        var detail = Render<HandoverFgDetailPage>(p => p.Add(x => x.Id, result.Id));
        detail.WaitForAssertion(() => Assert.Contains("ST-9", detail.Find("h1").TextContent));
        Assert.Contains("30", detail.Find("tbody").TextContent);
        detail.FindAll(".gms-pageheader-actions fluent-button, .gms-pageheader-actions button.ant-btn").Single(b => b.TextContent == "Void").Click();
        detail.WaitForAssertion(() => Assert.NotEmpty(Dialog(detail).QuerySelectorAll(".gms-impact")));
        Dialog(detail).QuerySelector("textarea")!.Input("salah");
        DialogConfirm(detail).Click();

        detail.WaitForAssertion(() => Assert.Contains("Di-void: salah", detail.Markup));
        Assert.Empty(detail.FindAll(".gms-pageheader-actions fluent-button, .gms-pageheader-actions button.ant-btn"));
    }

    [Fact]
    public void Stok_FG_menampilkan_ringkasan_baris_dan_tautan_kartu_stok()
    {
        var cut = Render<StockPage>();

        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));
        Assert.Contains("Fisik 480", cut.Find("[role=status]").TextContent);
        Assert.Contains("/stock/ledger?fgId=", cut.Find("tbody fluent-anchor, tbody a.ant-btn").GetAttribute("href"));
        cut.Find("#st-q").Change("zzz");
        cut.WaitForAssertion(() => Assert.Contains("Tidak ada stok", cut.Markup));
    }

    [Fact]
    public void Penyesuaian_stok_memeriksa_hasil_menahan_bila_negatif_dan_menyimpan_bila_aman()
    {
        var cut = Render<StockPage>();
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("tbody tr").Count));
        cut.FindAll(".gms-pageheader-actions fluent-button, .gms-pageheader-actions button.ant-btn").Single(b => b.TextContent == "Penyesuaian").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(Dialog(cut).QuerySelectorAll("select")));
        var selects = Dialog(cut).QuerySelectorAll("select");
        selects[0].Change(MockFgApi.KemejaId.ToString());
        Dialog(cut).QuerySelectorAll("select")[1].Change(Sync(_rig.Colors.LookupAsync("NVY"))[0].Id.ToString());
        Dialog(cut).QuerySelectorAll("select")[2].Change(Sync(_rig.Sizes.LookupAsync(null))[0].Id.ToString());
        Dialog(cut).QuerySelector("input[inputmode=numeric]")!.Input("-500");
        Dialog(cut).QuerySelectorAll("fluent-button, button.ant-btn").Single(b => b.TextContent == "Periksa hasil").Click();

        cut.WaitForAssertion(() => Assert.Contains("Stok fisik tidak boleh negatif", Dialog(cut).TextContent));
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));

        Dialog(cut).QuerySelector("input[inputmode=numeric]")!.Input("-10");
        Dialog(cut).QuerySelectorAll("fluent-button, button.ant-btn").Single(b => b.TextContent == "Periksa hasil").Click();
        Dialog(cut).QuerySelector("textarea")!.Input("rusak");
        cut.WaitForAssertion(() => Assert.False(DialogConfirm(cut).HasAttribute("disabled")));
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("110", cut.Find("tbody").TextContent));
    }

    [Fact]
    public async Task Kartu_stok_menampilkan_saldo_berjalan_dan_qty_negatif_bertanda()
    {
        var s = (await _rig.Inventory.ListStockAsync(new StockQuery())).Rows.Items[0];
        await _rig.Inventory.AdjustStockAsync(new AdjustStockRequest(s.FgId, s.ColorSize.ColorId, s.ColorSize.SizeId, -10, new DateOnly(2026, 11, 9), "rusak"));
        Nav.NavigateTo($"/stock/ledger?fgId={s.FgId}&colorId={s.ColorSize.ColorId}&sizeId={s.ColorSize.SizeId}");

        var cut = Render<StockLedgerPage>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Contains("−10", cut.Find("tbody tr").TextContent);
        Assert.Contains("110", cut.Find("tbody tr").TextContent);
        Assert.NotEmpty(cut.FindAll(".gms-negative"));
        Assert.Contains("Fisik 110", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void Kartu_stok_tanpa_parameter_menawarkan_kembali_ke_Stok_FG()
    {
        var cut = Render<StockLedgerPage>();

        Assert.Contains("Pilih baris stok", cut.Markup);
    }

    [Fact]
    public async Task Serah_Terima_Material_form_menyimpan_dan_daftar_dapat_Void()
    {
        var cut = Render<MaterialHandoverFormPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.FindAll("select")[1].Change((await _rig.Materials.LookupAsync(null))[0].Id.ToString());
        cut.Find("input[inputmode=decimal]").Input("120,5");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.EndsWith("/handovers/material", Nav.Uri));

        var list = Render<MaterialHandoverListPage>();
        list.WaitForAssertion(() => Assert.Single(list.FindAll("tbody tr")));
        list.FindAll("tbody fluent-button, tbody button.ant-btn").Single(b => b.TextContent == "Void").Click();
        Dialog(list).QuerySelector("textarea")!.Input("salah");
        DialogConfirm(list).Click();

        list.WaitForAssertion(() => Assert.DoesNotContain(list.FindAll("tbody fluent-button, tbody button.ant-btn"), b => b.TextContent == "Void"));
    }

    [Fact]
    public void Serah_Terima_Material_form_menolak_qty_tidak_valid_dengan_pesan()
    {
        var cut = Render<MaterialHandoverFormPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.FindAll("select")[1].Change(Sync(_rig.Materials.LookupAsync(null))[0].Id.ToString());
        cut.Find("input[inputmode=decimal]").Input("1.5");

        cut.Find("form").Submit();

        Assert.Contains("Gunakan koma untuk desimal", cut.Find(".gms-formsummary").TextContent);
    }

    [Fact]
    public void Impor_saldo_awal_memakai_wizard_dan_menambah_stok()
    {
        Services.AddSingleton<IImportApi>(new MockImportApi(_rig.Materials, _rig.Categories, _rig.Uoms, _rig.Workers, _rig.Processes, _rig.Fgs, _rig.Sizes, _rig.Customers, _rig.Colors, _rig.Inventory));
        Services.AddSingleton<Gms.Web.Infrastructure.Http.FileDownloader>();
        var cut = Render<StockImportPage>();

        cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>().UploadFiles(InputFileContent.CreateFromText("fg_code,color_code,size_code,qty,date\nKMJ-001,NVY,S,10,2026-11-01\n", "saldo.csv"));
        cut.WaitForAssertion(() => Assert.Equal("2. Periksa", cut.Find("[aria-current=step]").TextContent));
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Lanjut")).Click();
        cut.FindAll("fluent-button, button.ant-btn").Single(b => b.TextContent.Contains("Impor sekarang")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Impor selesai", cut.Markup));
        Assert.Equal(490, Sync(_rig.Inventory.ListStockAsync(new StockQuery())).OnHandTotal);
    }
}
