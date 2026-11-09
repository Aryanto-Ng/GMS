using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Gms.Web.Components.Pages.Production;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

/// <summary>Perangkaian mock produksi: Master → PO → WO → Produksi.</summary>
public sealed class ProductionRig
{
    public MockSizeApi Sizes { get; } = new();
    public MockProcessApi Processes { get; } = new();
    public MockCustomerApi Customers { get; } = new();
    public MockColorApi Colors { get; } = new();
    public MockWorkerApi Workers { get; }
    public MockVendorApi Vendors { get; } = new();
    public MockFgApi Fgs { get; }
    public MockPoApi Pos { get; }
    public MockWoApi Wos { get; }
    public MockProductionApi Production { get; }
    public MockUomApi Uoms { get; } = new();
    public MockMaterialCategoryApi Categories { get; } = new();
    public MockMaterialApi Materials { get; }
    public MockInventoryApi Inventory { get; }
    public MockDeliveriesApi Deliveries { get; }
    public MockPayrollApi Payroll { get; }

    public ProductionRig()
    {
        Workers = new MockWorkerApi(Processes);
        Fgs = new MockFgApi(Sizes, Processes, Customers);
        Pos = new MockPoApi(Customers, Fgs, Colors, Sizes);
        Wos = new MockWoApi(Pos, Fgs, Customers);
        Production = new MockProductionApi(Wos, Workers, Vendors);
        Materials = new MockMaterialApi(Uoms, Categories);
        Inventory = new MockInventoryApi(Pos, Wos, Production, Fgs, Colors, Sizes, Materials);
        Deliveries = new MockDeliveriesApi(Pos, Inventory, Customers);
        Payroll = new MockPayrollApi(Production, Wos, Workers, Processes, Sizes);
        Production.Today = Inventory.Today = Deliveries.Today = Payroll.Today = () => new DateOnly(2026, 11, 9);
    }

    public static Guid WoId => MockWoApi.SeedWoId;

    public WoDto Wo => Wos.GetAsync(WoId).GetAwaiter().GetResult();

    public Guid Line(int index) => Wo.Lines[index].PoLineId;

    public Guid Worker(int index) => Workers.LookupAsync(null).GetAwaiter().GetResult()[index].Id;

    public Guid VendorId => Vendors.LookupAsync(null).GetAwaiter().GetResult()[0].Id;

    public Task<SaveOutputsResult> Output(int seq, int qty, int line = 0, int worker = 0, bool confirm = false) =>
        Production.SaveOutputsAsync(new SaveOutputsRequest(WoId, seq, new DateOnly(2026, 11, 9), [new OutputRow(Worker(worker), [new WoLineQty(Line(line), qty)])], confirm));
}

public sealed class MockProductionTests
{
    private readonly ProductionRig _rig = new();

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    public MockProductionTests() => _rig.Production.Today = () => new DateOnly(2026, 11, 9);

    [Fact]
    public async Task Proses_pertama_bebas_tetapi_melebihi_Qty_WO_butuh_konfirmasi()
    {
        var ok = await _rig.Output(1, 50);
        Assert.Equal((50, 1), (ok.TotalQty, ok.WorkerCount));

        var needs = await Fails(() => _rig.Output(1, 1));
        Assert.Equal(ProblemCodes.OutputExceedsWoQtyNeedsConfirmation, needs.Problem.Code);
        Assert.Contains("kumulatif 51 dari 50", needs.Problem.Detail);

        var confirmed = await _rig.Output(1, 1, confirm: true);
        Assert.Contains("Melebihi Qty WO", Assert.Single(confirmed.Warnings));
        Assert.Equal(StepState.InProgress, (await _rig.Wos.GetProgressAsync(ProductionRig.WoId)).Steps[0].State); // target total 150, output 51
    }

    [Fact]
    public async Task Langkah_berikutnya_dibatasi_output_langkah_sebelumnya_dan_konteks_menyatakan_batas()
    {
        await _rig.Output(1, 50);
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, new DateOnly(2026, 11, 9), null, null, [new WoLineQty(_rig.Line(0), 50)]));

        var blocked = await Fails(() => _rig.Output(3, 1));
        Assert.Equal(ProblemCodes.OutputCumulativeExceeded, blocked.Problem.Code); // belum ada yang diterima dari vendor

        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Receive, new DateOnly(2026, 11, 9), null, null, [new WoLineQty(_rig.Line(0), 40)]));
        var ctx = await _rig.Production.GetInputContextAsync(ProductionRig.WoId, 3, new DateOnly(2026, 11, 9));
        Assert.Equal((40, false), (ctx.Columns[0].MaxAllowed, ctx.IsFirstStep));
        Assert.Equal(0, ctx.Columns[1].MaxAllowed);

        await _rig.Output(3, 40);
        Assert.Equal(0, (await _rig.Production.GetInputContextAsync(ProductionRig.WoId, 3, new DateOnly(2026, 11, 9))).Columns[0].MaxAllowed);
        var tooMuch = await Fails(() => _rig.Output(3, 1));
        var violation = Assert.Single(tooMuch.Problem.Violations!);
        Assert.Equal((41m, 40m), (violation.Current, violation.Limit));
    }

    [Fact]
    public async Task Simpan_semua_atau_tidak_sama_sekali_dan_validasi_input()
    {
        await _rig.Output(1, 10);
        var req = new SaveOutputsRequest(ProductionRig.WoId, 1, new DateOnly(2026, 11, 9), [new OutputRow(_rig.Worker(0), [new WoLineQty(_rig.Line(0), 5)]), new OutputRow(_rig.Worker(1), [new WoLineQty(_rig.Line(1), -1)])]);

        await Fails(() => _rig.Production.SaveOutputsAsync(req));
        Assert.Equal(10, (await _rig.Production.GetInputContextAsync(ProductionRig.WoId, 1, new DateOnly(2026, 11, 9))).CurrentTotal); // tidak ada yang tersimpan

        Assert.Contains("workDate", (await Fails(() => _rig.Production.SaveOutputsAsync(req with { WorkDate = new DateOnly(2026, 12, 1), Rows = [req.Rows[0]] }))).Problem.Errors!.Keys);
        Assert.Contains("rows", (await Fails(() => _rig.Production.SaveOutputsAsync(req with { Rows = [req.Rows[0], req.Rows[0]] }))).Problem.Errors!.Keys);
        Assert.Contains("seq", (await Fails(() => _rig.Output(2, 5))).Problem.Errors!.Keys); // langkah Vendor bukan output
    }

    [Fact]
    public async Task Void_ditahan_langkah_hilir_dengan_pratinjau_dan_berhasil_setelah_hilir_dikurangi()
    {
        await _rig.Output(1, 50);
        await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, new DateOnly(2026, 11, 9), null, null, [new WoLineQty(_rig.Line(0), 50)]));
        var receive = await _rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Receive, new DateOnly(2026, 11, 9), null, null, [new WoLineQty(_rig.Line(0), 40)]));
        var cut = (await _rig.Production.ListEntriesAsync(new EntryListQuery { Kind = EntryKind.Output })).Items.Single();

        var preview = await _rig.Production.PreviewVoidAsync(cut.Id);
        Assert.False(preview.CanProceed);
        var row = Assert.Single(preview.Rows);
        Assert.Equal((50m, 0m, 50m, false), (row.Before, row.After, row.Limit, row.Ok));
        Assert.Contains("Tertahan oleh Bordir (50)", row.Message);
        var blocked = await Fails(() => _rig.Production.VoidAsync(cut.Id, new ReasonRequest("salah")));
        Assert.Equal(ProblemCodes.OutputBelowDownstream, blocked.Problem.Code);

        await _rig.Production.VoidVendorMovementAsync(receive, new ReasonRequest("salah terima"));
        Assert.False((await _rig.Production.PreviewVoidAsync(cut.Id)).CanProceed); // 50 pcs masih tercatat terkirim ke vendor
        await Fails(() => _rig.Production.VoidAsync(cut.Id, new ReasonRequest(" ")));
    }

    [Fact]
    public async Task Void_output_tanpa_hilir_berhasil_mencatat_alasan_dan_menyembunyikan_dari_daftar_aktif()
    {
        await _rig.Output(1, 20);
        var entry = (await _rig.Production.ListEntriesAsync(new EntryListQuery())).Items.Single();
        Assert.True(entry.CanVoid);

        await _rig.Production.VoidAsync(entry.Id, new ReasonRequest("salah input"));

        Assert.Empty((await _rig.Production.ListEntriesAsync(new EntryListQuery())).Items);
        var all = (await _rig.Production.ListEntriesAsync(new EntryListQuery { IncludeVoided = true })).Items.Single();
        Assert.Equal((DocStatus.Voided, "salah input"), (all.Status, all.VoidReason));
        Assert.Equal(0, (await _rig.Wos.GetProgressAsync(ProductionRig.WoId)).Steps[0].TotalOutput);
        Assert.Equal(ProblemTypes.StateConflict, (await Fails(() => _rig.Production.VoidAsync(entry.Id, new ReasonRequest("lagi")))).Problem.Type);
    }

    [Fact]
    public async Task Koreksi_membuat_entri_pengganti_atomik_dengan_tautan_dan_pratinjau()
    {
        await _rig.Output(1, 20);
        var entry = (await _rig.Production.ListEntriesAsync(new EntryListQuery())).Items.Single();
        var request = new CorrectOutputRequest("salah jumlah", _rig.Worker(1), new DateOnly(2026, 11, 9), [new WoLineQty(_rig.Line(0), 25)]);

        var preview = await _rig.Production.PreviewCorrectOutputAsync(entry.Id, request);
        Assert.True(preview.CanProceed);
        Assert.Equal((20m, 25m), (preview.Rows[0].Before, preview.Rows[0].After));
        var newId = await _rig.Production.CorrectOutputAsync(entry.Id, request);

        var old = (await _rig.Production.GetEntryAsync(entry.Id)).Item;
        var replacement = (await _rig.Production.GetEntryAsync(newId)).Item;
        Assert.Equal((DocStatus.Voided, newId), (old.Status, old.ReplacedById));
        Assert.Equal((entry.Id, 25, "Dedi"), (replacement.ReplacesId, replacement.TotalQty, replacement.WorkerName));
        Assert.Equal(25, (await _rig.Production.GetInputContextAsync(ProductionRig.WoId, 1, new DateOnly(2026, 11, 9))).CurrentTotal);
    }

    [Fact]
    public async Task Status_WO_Completed_memblokir_input_dan_menonaktifkan_void_dengan_alasan()
    {
        await _rig.Output(1, 10, line: 0);
        var wo = _rig.Wo;
        await _rig.Wos.CompleteAsync(wo.Id, new WoActionRequest(null, wo.RowVersion));

        var entry = (await _rig.Production.ListEntriesAsync(new EntryListQuery())).Items.Single();
        Assert.False(entry.CanVoid);
        Assert.Contains("Completed", entry.DisabledReason);
        Assert.Contains("berstatus Completed", (await _rig.Production.GetInputContextAsync(ProductionRig.WoId, 1, new DateOnly(2026, 11, 9))).BlockedReason);
        Assert.Equal(ProblemCodes.WoStatusInvalid, (await Fails(() => _rig.Output(1, 1))).Problem.Code);
    }

    [Fact]
    public async Task Periode_tertutup_memblokir_input_dan_penyesuaian_hanya_untuk_tanggal_asal_di_periode_tertutup()
    {
        await _rig.Output(1, 30);
        _rig.Production.ClosedUntil = () => new DateOnly(2026, 11, 9);

        Assert.Equal(ProblemTypes.PeriodClosed, (await Fails(() => _rig.Output(1, 1))).Problem.Type);
        var adjust = new AdjustmentRequest(ProductionRig.WoId, 1, _rig.Worker(0), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 9), "salah hitung", [new WoLineQty(_rig.Line(0), -5)]);
        _rig.Production.ClosedUntil = () => new DateOnly(2026, 11, 1);
        Assert.Contains("originalDate", (await Fails(() => _rig.Production.CreateAdjustmentAsync(adjust))).Problem.Errors!.Keys); // asal di periode terbuka

        _rig.Production.ClosedUntil = () => new DateOnly(2026, 11, 8);
        await _rig.Production.CreateAdjustmentAsync(adjust);
        Assert.Equal(25, (await _rig.Production.GetInputContextAsync(ProductionRig.WoId, 1, new DateOnly(2026, 11, 9))).CurrentTotal);
        Assert.Equal(EntryKind.Adjustment, (await _rig.Production.ListEntriesAsync(new EntryListQuery { Kind = EntryKind.Adjustment })).Items.Single().Kind);
    }

    [Fact]
    public async Task Vendor_kirim_terima_dibatasi_per_vendor_dan_ringkasan_sisa()
    {
        await _rig.Output(1, 50);
        SaveVendorMovementRequest Move(VendorMovementKind kind, int qty) => new(ProductionRig.WoId, 2, _rig.VendorId, kind, new DateOnly(2026, 11, 9), "SJ-1", null, [new WoLineQty(_rig.Line(0), qty)]);

        Assert.Equal(ProblemCodes.VendorSentExceeded, (await Fails(() => _rig.Production.SaveVendorMovementAsync(Move(VendorMovementKind.Send, 51)))).Problem.Code);
        await _rig.Production.SaveVendorMovementAsync(Move(VendorMovementKind.Send, 30));
        Assert.Equal(ProblemCodes.VendorReceivedExceeded, (await Fails(() => _rig.Production.SaveVendorMovementAsync(Move(VendorMovementKind.Receive, 31)))).Problem.Code);
        await _rig.Production.SaveVendorMovementAsync(Move(VendorMovementKind.Receive, 10));

        var ctx = await _rig.Production.GetVendorContextAsync(ProductionRig.WoId, 2, _rig.VendorId);
        Assert.Equal((20, 20, 30, 10), (ctx.Columns[0].MaxSend, ctx.Columns[0].MaxReceive, ctx.Columns[0].SentTotal, ctx.Columns[0].ReceivedTotal));
        var summary = Assert.Single(ctx.Vendors);
        Assert.Equal((30, 10, 20), (summary.Sent, summary.Received, summary.Outstanding));
        Assert.Contains("seq", (await Fails(() => _rig.Production.GetVendorContextAsync(ProductionRig.WoId, 1, null))).Problem.Errors!.Keys);
        Assert.Equal(20, (await _rig.Wos.GetAsync(ProductionRig.WoId)).OutstandingAtVendorQty);
    }
}

public sealed class ProductionPageTests : WebTestBase
{
    private static readonly string[] AllProd = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("ProductionOutput.", StringComparison.Ordinal) || p.Key.StartsWith("VendorJob.", StringComparison.Ordinal) || p.Key.StartsWith("WorkOrder.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly ProductionRig _rig = new();

    public ProductionPageTests()
    {
        _rig.Production.Today = () => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        Services.AddSingleton<IProductionApi>(_rig.Production);
        Services.AddSingleton<IWorkOrdersApi>(_rig.Wos);
        Services.AddSingleton<IWorkerApi>(_rig.Workers);
        Services.AddSingleton<IVendorApi>(_rig.Vendors);
        SignIn(AllProd);
    }

    private static IElement Dialog<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null);

    private static IElement DialogConfirm<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent => Dialog(cut).QuerySelectorAll("fluent-button").Last();

    private void PickWoAndStep(IRenderedComponent<InputOutputPage> cut, string stepLabel)
    {
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[1].QuerySelectorAll("option").Length > 1));
        var option = cut.FindAll("select")[1].QuerySelectorAll("option").Single(o => o.TextContent.Contains(stepLabel));
        cut.FindAll("select")[1].Change(option.GetAttribute("value"));
        cut.WaitForAssertion(() => Assert.Contains("Tambah pekerja", cut.Markup));
    }

    [Fact]
    public void Input_Output_awal_meminta_pilih_WO_dan_proses_hanya_menawarkan_langkah_Internal()
    {
        var cut = Render<InputOutputPage>();

        cut.WaitForAssertion(() => Assert.Contains("Pilih WO dan proses untuk mulai", cut.Markup));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.WaitForAssertion(() => Assert.Equal(["Pilih proses…", "1 Cutting", "3 Sewing", "4 QC"], cut.FindAll("select")[1].QuerySelectorAll("option").Select(o => o.TextContent.Trim())));
    }

    [Fact]
    public void Input_Output_proses_pertama_menampilkan_info_menambah_pekerja_dan_menyimpan_lalu_mengosongkan_baris()
    {
        var cut = Render<InputOutputPage>();
        PickWoAndStep(cut, "1 Cutting");

        Assert.Contains("Proses pertama: bebas", cut.Markup);
        cut.FindAll("select")[2].Change(_rig.Worker(0).ToString());
        cut.FindAll("fluent-button").Single(b => b.TextContent == "Tambah pekerja").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));
        cut.Find("table.gms-matrix input[aria-label='PKJ-001 Wati, Size Navy S']").Input("40");
        Assert.Contains("1 pekerja · 40 pcs", cut.Markup);
        Assert.Contains("Belum disimpan", cut.Markup);
        cut.FindAll("fluent-button").Single(b => b.TextContent == "Simpan").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("table.gms-matrix")));
        Assert.Equal(40, Sync(_rig.Production.GetInputContextAsync(ProductionRig.WoId, 1, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)))).CurrentTotal);
    }

    [Fact]
    public void Input_Output_melebihi_batas_langkah_sebelumnya_ditandai_galat_pada_sel_dengan_batas_server()
    {
        var cut = Render<InputOutputPage>();
        PickWoAndStep(cut, "3 Sewing");
        cut.FindAll("select")[2].Change(_rig.Worker(0).ToString());
        cut.FindAll("fluent-button").Single(b => b.TextContent == "Tambah pekerja").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));

        Assert.Contains("maks 0", cut.Find("table.gms-matrix").TextContent);
        cut.Find("table.gms-matrix input[aria-label='PKJ-001 Wati, Size Navy S']").Input("5");

        Assert.Equal("true", cut.Find("table.gms-matrix input[aria-label='PKJ-001 Wati, Size Navy S']").GetAttribute("aria-invalid"));
        Assert.Contains("melebihi batas", cut.Find(".gms-cellmsg").TextContent);
    }

    [Fact]
    public void Input_Output_lebih_dari_Qty_WO_meminta_konfirmasi_lalu_menyimpan()
    {
        var cut = Render<InputOutputPage>();
        PickWoAndStep(cut, "1 Cutting");
        cut.FindAll("select")[2].Change(_rig.Worker(0).ToString());
        cut.FindAll("fluent-button").Single(b => b.TextContent == "Tambah pekerja").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));
        cut.Find("table.gms-matrix input[aria-label='PKJ-001 Wati, Size Navy S']").Input("51");

        cut.FindAll("fluent-button").Single(b => b.TextContent == "Simpan").Click();
        cut.WaitForAssertion(() => Assert.Contains("kumulatif 51 dari 50", Dialog(cut).TextContent));
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("table.gms-matrix")));
    }

    [Fact]
    public void Riwayat_menampilkan_entri_void_dicoret_dan_dialog_Void_memuat_pratinjau_dampak_yang_menahan()
    {
        Sync(_rig.Output(1, 50));
        Sync(_rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Send, new DateOnly(2026, 11, 9), null, null, [new WoLineQty(_rig.Line(0), 50)])));
        Sync(_rig.Production.SaveVendorMovementAsync(new SaveVendorMovementRequest(ProductionRig.WoId, 2, _rig.VendorId, VendorMovementKind.Receive, new DateOnly(2026, 11, 9), null, null, [new WoLineQty(_rig.Line(0), 40)])));
        var cut = Render<ProductionHistoryPage>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("tbody tr").Count));

        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Output")).QuerySelectorAll("fluent-button").Single(b => b.TextContent == "Void").Click();

        cut.WaitForAssertion(() => Assert.Contains("Tertahan oleh Bordir (50)", Dialog(cut).TextContent));
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Riwayat_Void_berhasil_dengan_alasan_dan_Tampilkan_Void_memunculkan_baris_dicoret()
    {
        Sync(_rig.Output(1, 20));
        var cut = Render<ProductionHistoryPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));

        cut.FindAll("tbody fluent-button").Single(b => b.TextContent == "Void").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(Dialog(cut).QuerySelectorAll(".gms-impact")));
        Dialog(cut).QuerySelector("textarea")!.Input("salah input");
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("tbody tr")));
        cut.Find("input[type=checkbox]").Change(true);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.NotEmpty(cut.FindAll("tbody s"));
        Assert.Contains("Void", cut.Find("tbody").TextContent);
    }

    [Fact]
    public void Riwayat_aksi_nonaktif_dengan_alasan_untuk_WO_Completed()
    {
        Sync(_rig.Output(1, 10));
        var wo = _rig.Wo;
        Sync(_rig.Wos.CompleteAsync(wo.Id, new WoActionRequest(null, wo.RowVersion)));
        var cut = Render<ProductionHistoryPage>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));

        var voidButton = cut.FindAll("tbody fluent-button").Single(b => b.TextContent == "Void");

        Assert.True(voidButton.HasAttribute("disabled"));
        Assert.Contains("Completed", voidButton.GetAttribute("title"));
    }

    [Fact]
    public void Vendor_Kirim_menyimpan_dan_tab_Terima_tanpa_barang_menampilkan_state_kosong()
    {
        Sync(_rig.Output(1, 50));
        var cut = Render<VendorMovementPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[1].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[1].Change(cut.FindAll("select")[1].QuerySelectorAll("option")[1].GetAttribute("value"));
        cut.FindAll("select")[2].Change(_rig.VendorId.ToString());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));

        Assert.Contains("maks 50", cut.Find("table.gms-matrix").TextContent);
        cut.Find("table.gms-matrix input[aria-label='Qty, Size Navy S']").Input("30");
        cut.FindAll("fluent-button").Single(b => b.TextContent == "Simpan").Click();
        cut.WaitForAssertion(() => Assert.Equal(30, Sync(_rig.Wos.GetAsync(ProductionRig.WoId)).OutstandingAtVendorQty));

        cut.FindAll("[role=tab]").Single(t => t.TextContent == "Terima").Click();
        cut.WaitForAssertion(() => Assert.Contains("maks 30", cut.Find("table.gms-matrix").TextContent));
    }

    [Fact]
    public void Penyesuaian_menolak_tanggal_asal_di_periode_terbuka_dengan_pesan_server()
    {
        Sync(_rig.Output(1, 30));
        var cut = Render<AdjustmentPage>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[0].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[0].Change(ProductionRig.WoId.ToString());
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("select")[1].QuerySelectorAll("option").Length > 1));
        cut.FindAll("select")[1].Change(cut.FindAll("select")[1].QuerySelectorAll("option")[1].GetAttribute("value"));
        cut.FindAll("select")[2].Change(_rig.Worker(0).ToString());
        cut.FindAll("input[type=date]")[0].Input("2026-11-01");
        cut.Find("textarea").Input("salah hitung");
        cut.FindAll("input[inputmode=numeric]")[0].Input("-5");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Tanggal asal harus berada pada periode gaji tertutup", cut.Find(".gms-formsummary").TextContent));
    }
}
