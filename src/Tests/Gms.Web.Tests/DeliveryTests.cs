using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Deliveries;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Gms.Web.Components.Pages.Sales;
using Gms.Web.Infrastructure.Http;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class MockDeliveriesTests
{
    private readonly ProductionRig _rig = new();

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    private async Task<PoLineDto> AllocatedLine(int qty, int index = 0)
    {
        var line = (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).Lines[index];
        await _rig.Pos.AllocateAsync(line.Id, new AllocateRequest(qty, null));
        return line;
    }

    private static SaveDeliveryRequest Req(Guid lineId, int qty, DateOnly? date = null) =>
        new(MockPoApi.SeedPoId, date ?? new DateOnly(2026, 11, 9), "Budi", "B 1234 XY", "Andi", null, [new DeliveryLineQty(lineId, qty)]);

    [Fact]
    public async Task PO_layak_kirim_dan_konteks_menunjukkan_maks_kirim_dari_alokasi()
    {
        var line = await AllocatedLine(40);

        var eligible = Assert.Single(await _rig.Deliveries.ListEligiblePosAsync(null));
        Assert.Equal(("PO-202611-0001", 500), (eligible.Code, eligible.OpenQty));
        var ctx = await _rig.Deliveries.GetContextAsync(MockPoApi.SeedPoId);
        var option = ctx.Lines.Single(l => l.PoLineId == line.Id);
        Assert.Equal((100, 40, 40, 80), (option.Qty, option.AllocatedQty, option.MaxDeliver, option.StockAvailable)); // stok fisik 120 − teralokasi 40
        Assert.Equal(0, ctx.Lines.First(l => l.PoLineId != line.Id).MaxDeliver);
    }

    [Fact]
    public async Task Kirim_mengurangi_stok_dan_alokasi_menambah_Terkirim_dan_mengalir_ke_kartu_stok()
    {
        var line = await AllocatedLine(40);

        var doc = await _rig.Deliveries.CreateAsync(Req(line.Id, 30));

        Assert.StartsWith("SJ-202611-", doc.Item.Code);
        var po = await _rig.Pos.GetAsync(MockPoApi.SeedPoId);
        Assert.Equal((30, 10), (po.Lines[0].DeliveredQty, po.Lines[0].AllocatedQty));
        var stock = (await _rig.Inventory.ListStockAsync(new StockQuery())).Rows.Items.Single(r => r.ColorSize.SizeId == line.ColorSize.SizeId);
        Assert.Equal((90, 10), (stock.OnHand, stock.Allocated));
        var ledger = await _rig.Inventory.GetStockLedgerAsync(new StockLedgerQuery { FgId = line.FgId, ColorId = line.ColorSize.ColorId, SizeId = line.ColorSize.SizeId });
        Assert.Equal((StockLedgerKind.Delivery, -30, 90), (ledger.Rows.Items[0].Kind, ledger.Rows.Items[0].Qty, ledger.Rows.Items[0].RunningBalance));
    }

    [Fact]
    public async Task Qty_melebihi_batas_ditolak_dengan_batas_terbaru_dan_validasi_dasar()
    {
        var line = await AllocatedLine(40);

        var over = await Fails(() => _rig.Deliveries.CreateAsync(Req(line.Id, 41)));
        Assert.Equal(ProblemCodes.DeliveryExceedsAllocation, over.Problem.Code);
        Assert.Equal((41m, 40m), (over.Problem.Violations![0].Current, over.Problem.Violations[0].Limit));
        Assert.Contains("deliveryDate", (await Fails(() => _rig.Deliveries.CreateAsync(Req(line.Id, 1, new DateOnly(2026, 12, 1))))).Problem.Errors!.Keys);
        Assert.Contains("lines", (await Fails(() => _rig.Deliveries.CreateAsync(Req(line.Id, 0)))).Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Pengiriman_penuh_menyelesaikan_PO_dan_void_mengembalikan_stok_alokasi_dan_status()
    {
        var po = await _rig.Pos.GetAsync(MockPoApi.SeedPoId);
        foreach (var l in po.Lines) { await _rig.Pos.AllocateAsync(l.Id, new AllocateRequest(Math.Min(l.RemainingPlanQty, 120), null)); }
        po = await _rig.Pos.GetAsync(MockPoApi.SeedPoId);
        var lines = po.Lines.Where(l => l.AllocatedQty > 0).Select(l => new DeliveryLineQty(l.Id, l.AllocatedQty)).ToList();
        var doc = await _rig.Deliveries.CreateAsync(new SaveDeliveryRequest(po.Id, new DateOnly(2026, 11, 9), null, null, null, null, lines));
        var delivered = (await _rig.Pos.GetAsync(po.Id)).DeliveredQty;
        Assert.Equal(lines.Sum(l => l.Qty), delivered);

        var preview = await _rig.Deliveries.PreviewVoidAsync(doc.Item.Id);
        Assert.True(preview.CanProceed);
        var voided = await _rig.Deliveries.VoidAsync(doc.Item.Id, new ReasonRequest("salah kirim"));

        Assert.Equal((DocStatus.Voided, "salah kirim"), (voided.Item.Status, voided.VoidReason));
        var after = await _rig.Pos.GetAsync(po.Id);
        Assert.Equal(0, after.DeliveredQty);
        Assert.Equal(delivered, after.AllocatedQty);
        Assert.Equal(ProblemTypes.StateConflict, (await Fails(() => _rig.Deliveries.VoidAsync(doc.Item.Id, new ReasonRequest("lagi")))).Problem.Type);
    }

    [Fact]
    public async Task PO_Completed_kembali_In_Progress_saat_pengiriman_di_void()
    {
        var po = await _rig.Pos.GetAsync(MockPoApi.SeedPoId);
        foreach (var l in po.Lines) { _rig.Pos.AdjustStock(l.FgId, l.ColorSize.ColorId, l.ColorSize.SizeId, 200); }
        // Hanya baris yang masih memiliki Sisa Rencana dapat dialokasikan; WO contoh menahan sebagian — selesaikan lewat pengait agar PO lengkap.
        foreach (var l in po.Lines) { _rig.Pos.AdjustCommitted(l.Id, deltaAllocated: l.Qty - l.AllocatedQty - l.InProductionQty - l.DeliveredQty + l.InProductionQty); }
        po = await _rig.Pos.GetAsync(po.Id);
        var doc = await _rig.Deliveries.CreateAsync(new SaveDeliveryRequest(po.Id, new DateOnly(2026, 11, 9), null, null, null, null, po.Lines.Select(l => new DeliveryLineQty(l.Id, Math.Min(l.AllocatedQty, l.Qty))).ToList()));
        Assert.Equal(PoStatus.Completed, (await _rig.Pos.GetAsync(po.Id)).Status);

        Assert.Contains("Completed → In Progress", (await _rig.Deliveries.PreviewVoidAsync(doc.Item.Id)).Notes[^1]);
        await _rig.Deliveries.VoidAsync(doc.Item.Id, new ReasonRequest("batal"));

        Assert.Equal(PoStatus.InProgress, (await _rig.Pos.GetAsync(po.Id)).Status);
    }

    [Fact]
    public async Task Koreksi_membuat_pengganti_dengan_pratinjau_batas_pada_kondisi_akhir()
    {
        var line = await AllocatedLine(40);
        var doc = await _rig.Deliveries.CreateAsync(Req(line.Id, 30));
        var correct = new CorrectDeliveryRequest("salah jumlah", new DateOnly(2026, 11, 9), "Budi", null, null, null, [new DeliveryLineQty(line.Id, 40)]);

        Assert.True((await _rig.Deliveries.PreviewCorrectAsync(doc.Item.Id, correct)).CanProceed); // batas akhir 40 (30 dikembalikan)
        var tooMany = await _rig.Deliveries.PreviewCorrectAsync(doc.Item.Id, correct with { Lines = [new DeliveryLineQty(line.Id, 41)] });
        Assert.False(tooMany.CanProceed);
        Assert.Contains("Maksimal 40", tooMany.Rows[0].Message);

        var replacement = await _rig.Deliveries.CorrectAsync(doc.Item.Id, correct);
        Assert.Equal((doc.Item.Id, 40), (replacement.ReplacesId, replacement.Item.TotalQty));
        var old = await _rig.Deliveries.GetAsync(doc.Item.Id);
        Assert.Equal((DocStatus.Voided, replacement.Item.Id), (old.Item.Status, old.ReplacedById));
        Assert.Equal(40, (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).Lines[0].DeliveredQty);
        Assert.Contains("reason", (await Fails(() => _rig.Deliveries.CorrectAsync(replacement.Item.Id, correct with { Reason = " " }))).Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Edit_info_memakai_rowVersion_dan_PDF_menaikkan_jumlah_cetak_serta_menandai_cetak_ulang_dan_dibatalkan()
    {
        var line = await AllocatedLine(40);
        var doc = await _rig.Deliveries.CreateAsync(Req(line.Id, 10));

        var updated = await _rig.Deliveries.UpdateInfoAsync(doc.Item.Id, new UpdateDeliveryInfoRequest(new DateOnly(2026, 11, 8), "Citra", "D 9 X", null, "catatan", doc.RowVersion));
        Assert.Equal(("Citra", doc.RowVersion + 1), (updated.RecipientName, updated.RowVersion));
        Assert.Equal(409, (await Fails(() => _rig.Deliveries.UpdateInfoAsync(doc.Item.Id, new UpdateDeliveryInfoRequest(new DateOnly(2026, 11, 8), null, null, null, null, doc.RowVersion)))).StatusCode);

        var first = await _rig.Deliveries.GetPdfAsync(doc.Item.Id);
        var second = await _rig.Deliveries.GetPdfAsync(doc.Item.Id);
        Assert.Equal(($"{doc.Item.Code}.pdf", "application/pdf"), (first.FileName, first.ContentType));
        Assert.DoesNotContain("CETAK ULANG", System.Text.Encoding.ASCII.GetString(first.Bytes));
        Assert.Contains("CETAK ULANG", System.Text.Encoding.ASCII.GetString(second.Bytes));
        Assert.Equal(2, (await _rig.Deliveries.GetAsync(doc.Item.Id)).Item.PrintCount);
        await _rig.Deliveries.VoidAsync(doc.Item.Id, new ReasonRequest("batal"));
        Assert.Contains("DIBATALKAN", System.Text.Encoding.ASCII.GetString((await _rig.Deliveries.GetPdfAsync(doc.Item.Id)).Bytes));
    }

    [Fact]
    public async Task Void_serah_terima_ditolak_setelah_barang_dikirim_dan_stok_dikurangi()
    {
        await _rig.Output(1, 30);
        var date = new DateOnly(2026, 11, 9);
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, date, null, null, [new WoLineQty(_rig.Line(0), 30)]));
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Receive, date, null, null, [new WoLineQty(_rig.Line(0), 30)]));
        await _rig.Output(3, 30);
        await _rig.Output(4, 30);
        var handover = await _rig.Inventory.CreateHandoverFgAsync(new SaveHandoverFgRequest(ProductionRig.WoId, date, "ST", null, null, [new WoLineQty(_rig.Line(0), 30)]));
        var line = (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        await _rig.Deliveries.CreateAsync(Req(line.Id, 30));
        await _rig.Inventory.AdjustStockAsync(new AdjustStockRequest(line.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId, -100, date, "rusak"));

        var preview = await _rig.Inventory.PreviewVoidHandoverFgAsync(handover.Id);

        Assert.False(preview.CanProceed);
        Assert.Contains("Stok fisik tidak cukup", preview.Rows[0].Message);
    }
}

public sealed class DeliveryPageTests : WebTestBase
{
    private static readonly string[] AllDel = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("Pengiriman.", StringComparison.Ordinal) || p.Key.StartsWith("CustomerPO.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly ProductionRig _rig = new();

    public DeliveryPageTests()
    {
        _rig.Deliveries.Today = _rig.Inventory.Today = _rig.Production.Today = () => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Services.AddSingleton<IDeliveriesApi>(_rig.Deliveries);
        Services.AddSingleton<IPurchaseOrdersApi>(_rig.Pos);
        Services.AddSingleton<FileDownloader>();
        SignIn(AllDel);
    }

    private static IElement Dialog<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => cut.FindAll(".ant-modal-wrap").Last(d => d.QuerySelector("h2") is not null);

    private static IElement DialogConfirm<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => Dialog(cut).QuerySelectorAll("button.ant-btn").Last();

    private async Task<DeliveryDto> SeedDeliveryAsync(int allocate = 40, int ship = 30)
    {
        var line = (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        await _rig.Pos.AllocateAsync(line.Id, new AllocateRequest(allocate, null));
        return await _rig.Deliveries.CreateAsync(new SaveDeliveryRequest(MockPoApi.SeedPoId, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), "Budi", null, null, null, [new DeliveryLineQty(line.Id, ship)]));
    }

    [Fact]
    public async Task Daftar_menampilkan_surat_jalan_dan_filter_status()
    {
        var doc = await SeedDeliveryAsync();
        var cut = Render<DeliveryListPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains(doc.Item.Code, cut.Find("tbody").TextContent);
        cut.Find("#dl-status").Change("Voided");
        cut.WaitForAssertion(() => Assert.Contains("Belum ada surat jalan", cut.Markup));
    }

    [Fact]
    public async Task Form_memuat_baris_dengan_maks_kirim_menandai_qty_berlebih_dan_menyimpan()
    {
        var line = (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).Lines[0];
        await _rig.Pos.AllocateAsync(line.Id, new AllocateRequest(40, null));
        var cut = Render<DeliveryFormPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(MockPoApi.SeedPoId.ToString());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.Find("input[aria-label='Qty kirim KMJ-001 Navy S']").Input("41");
        Assert.Equal("true", cut.Find("input[aria-label='Qty kirim KMJ-001 Navy S']").GetAttribute("aria-invalid"));
        Assert.Contains("Maks 40", cut.Find(".gms-cellmsg").TextContent);
        cut.Find("input[aria-label='Qty kirim KMJ-001 Navy S']").Input("40");
        Assert.Contains("Total kirim: 40 pcs", cut.Markup);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Matches("/deliveries/[0-9a-f-]{36}$", Nav.Uri));
        Assert.Equal(40, (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).DeliveredQty);
    }

    [Fact]
    public void Form_baris_tanpa_alokasi_menawarkan_Alokasikan_stok_yang_membuka_dialog_dan_memperbarui_batas()
    {
        var cut = Render<DeliveryFormPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(MockPoApi.SeedPoId.ToString());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.FindAll("tbody button.ant-btn").First(b => b.TextContent.Trim() == "Alokasikan stok").Click();
        cut.WaitForAssertion(() => Assert.Contains("Tersedia 120", Dialog(cut).TextContent));
        Dialog(cut).QuerySelector("input")!.Input("25");
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody input")));
        Assert.Contains("25", cut.FindAll("tbody tr")[0].TextContent);
    }

    [Fact]
    public async Task Form_tanpa_qty_menampilkan_galat_dan_tidak_menyimpan()
    {
        await SeedDeliveryAsync(40, 10);
        var cut = Render<DeliveryFormPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(MockPoApi.SeedPoId.ToString());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.Find("form").Submit();

        Assert.Contains("Isi minimal satu baris", cut.Find(".gms-formsummary").TextContent);
    }

    [Fact]
    public async Task Detail_menampilkan_baris_aksi_cetak_PDF_menaikkan_jumlah_cetak()
    {
        var doc = await SeedDeliveryAsync();
        var cut = Render<DeliveryDetailPage>(p => p.Add(x => x.Id, doc.Item.Id));
        cut.WaitForAssertion(() => Assert.Contains(doc.Item.Code, cut.Find("h1").TextContent));

        Assert.Contains("Dicetak 0 kali", cut.Markup);
        cut.FindAll(".gms-pageheader-actions button.ant-btn").Single(b => b.TextContent.Trim() == "Cetak PDF").Click();

        cut.WaitForAssertion(() => Assert.Contains("Dicetak 1 kali", cut.Markup));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "save" && ((string)i.Arguments[0]!).EndsWith(".pdf"));
    }

    [Fact]
    public async Task Detail_Void_memuat_pratinjau_dan_menampilkan_banner_serta_menyembunyikan_aksi()
    {
        var doc = await SeedDeliveryAsync();
        var cut = Render<DeliveryDetailPage>(p => p.Add(x => x.Id, doc.Item.Id));
        cut.WaitForAssertion(() => Assert.Contains(doc.Item.Code, cut.Find("h1").TextContent));

        cut.FindAll(".gms-pageheader-actions button.ant-btn").Single(b => b.TextContent.Trim() == "Void").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(Dialog(cut).QuerySelectorAll(".gms-impact")));
        Dialog(cut).QuerySelector("textarea")!.Input("salah kirim");
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Di-void: salah kirim", cut.Markup));
        Assert.DoesNotContain(cut.FindAll(".gms-pageheader-actions button.ant-btn"), b => b.TextContent is "Void" or "Koreksi" or "Edit Info");
    }

    [Fact]
    public async Task Detail_Koreksi_memeriksa_dampak_menahan_bila_melebihi_dan_menyimpan_pengganti()
    {
        var doc = await SeedDeliveryAsync();
        var cut = Render<DeliveryDetailPage>(p => p.Add(x => x.Id, doc.Item.Id));
        cut.WaitForAssertion(() => Assert.Contains(doc.Item.Code, cut.Find("h1").TextContent));

        cut.FindAll(".gms-pageheader-actions button.ant-btn").Single(b => b.TextContent.Trim() == "Koreksi").Click();
        Dialog(cut).QuerySelector("input[inputmode=numeric]")!.Input("41");
        Dialog(cut).QuerySelectorAll("button.ant-btn").Single(b => b.TextContent.Trim() == "Periksa dampak").Click();
        cut.WaitForAssertion(() => Assert.Contains("Maksimal 40", Dialog(cut).TextContent));
        Dialog(cut).QuerySelector("textarea")!.Input("salah");
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));

        Dialog(cut).QuerySelector("input[inputmode=numeric]")!.Input("35");
        Dialog(cut).QuerySelectorAll("button.ant-btn").Single(b => b.TextContent.Trim() == "Periksa dampak").Click();
        cut.WaitForAssertion(() => Assert.False(DialogConfirm(cut).HasAttribute("disabled")));
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Matches("/deliveries/[0-9a-f-]{36}$", Nav.Uri));
        Assert.Equal(35, (await _rig.Pos.GetAsync(MockPoApi.SeedPoId)).DeliveredQty);
    }

    [Fact]
    public async Task Detail_Edit_Info_menyimpan()
    {
        var doc = await SeedDeliveryAsync();
        var cut = Render<DeliveryDetailPage>(p => p.Add(x => x.Id, doc.Item.Id));
        cut.WaitForAssertion(() => Assert.Contains(doc.Item.Code, cut.Find("h1").TextContent));
        cut.FindAll(".gms-pageheader-actions button.ant-btn").Single(b => b.TextContent.Trim() == "Edit Info").Click();
        Dialog(cut).QuerySelectorAll("input")[1].Input("Citra");
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("Penerima: Citra", cut.Markup));
    }

    [Fact]
    public async Task Tanpa_izin_aksi_detail_tidak_dirender()
    {
        var doc = await SeedDeliveryAsync();
        SignIn(Permissions.Pengiriman.View);

        var cut = Render<DeliveryDetailPage>(p => p.Add(x => x.Id, doc.Item.Id));

        cut.WaitForAssertion(() => Assert.Contains(doc.Item.Code, cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll(".gms-pageheader-actions *"));
    }
}
