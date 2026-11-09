using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Gms.Web.Components.Pages.Sales;
using Gms.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

public sealed class MockWoTests
{
    private readonly MockSizeApi _sizes = new();
    private readonly MockCustomerApi _customers = new();
    private readonly MockFgApi _fgs;
    private readonly MockPoApi _pos;
    private readonly MockWoApi _wos;

    public MockWoTests()
    {
        _fgs = new MockFgApi(_sizes, new MockProcessApi(), _customers);
        _pos = new MockPoApi(_customers, _fgs, new MockColorApi(), _sizes);
        _wos = new MockWoApi(_pos, _fgs, _customers);
    }

    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    private async Task<WoDto> NewDraft(int qty = 20, int lineIndex = 2)
    {
        var po = await _pos.GetAsync(MockPoApi.SeedPoId);
        return await _wos.CreateAsync(new SaveWoRequest(po.Id, MockFgApi.KemejaId, null, null, null, [new SaveWoLine(po.Lines[lineIndex].Id, qty)]));
    }

    [Fact]
    public async Task WO_contoh_sudah_Released_dan_menambah_Dalam_Produksi_pada_PO()
    {
        var wo = await _wos.GetAsync(MockWoApi.SeedWoId);

        Assert.Equal(WoStatus.Released, wo.Status);
        Assert.Equal(wo.Lines.Sum(l => l.Qty), (await _pos.GetAsync(MockPoApi.SeedPoId)).InProductionQty);
        Assert.Contains(WoActions.EditQuantities, wo.AllowedActions);
    }

    [Fact]
    public async Task Opsi_pembuatan_menampilkan_Sisa_Rencana_per_sel_dan_kesiapan_FG()
    {
        var options = await _wos.GetCreationOptionsAsync(MockPoApi.SeedPoId);

        var fg = Assert.Single(options.Fgs);
        Assert.True(fg.IsReady);
        Assert.Equal(4, fg.Cells.Count);
        Assert.Equal(500 - 150, fg.Cells.Sum(c => c.RemainingPlanQty));
    }

    [Fact]
    public async Task Buat_WO_menolak_qty_melebihi_sisa_target_sebelum_mulai_dan_kosong()
    {
        var po = await _pos.GetAsync(MockPoApi.SeedPoId);
        var line = po.Lines[2]; // sisa 150
        SaveWoRequest Req(int qty, DateOnly? start = null, DateOnly? target = null) => new(po.Id, MockFgApi.KemejaId, start, target, null, [new SaveWoLine(line.Id, qty)]);

        var over = await Fails(() => _wos.CreateAsync(Req(151)));
        Assert.Equal(ProblemCodes.PoRemainingPlanExceeded, over.Problem.Code);
        Assert.Equal((151m, 150m), (over.Problem.Violations![0].Current, over.Problem.Violations[0].Limit));
        Assert.Contains("targetDate", (await Fails(() => _wos.CreateAsync(Req(10, new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 1))))).Problem.Errors!.Keys);
        Assert.Contains("lines", (await Fails(() => _wos.CreateAsync(Req(0)))).Problem.Errors!.Keys);
        Assert.Equal(WoStatus.Draft, (await _wos.CreateAsync(Req(150))).Status);
    }

    [Fact]
    public async Task Rilis_membekukan_snapshot_menaikkan_Dalam_Produksi_dan_snapshot_tarif_tersedia()
    {
        var draft = await NewDraft(20);
        var preview = await _wos.PreviewReleaseAsync(draft.Id);
        Assert.True(preview.CanRelease);
        Assert.Equal((4, 12), (preview.RoutingStepCount, preview.TariffCellCount));
        var before = (await _pos.GetAsync(MockPoApi.SeedPoId)).InProductionQty;

        var released = await _wos.ReleaseAsync(draft.Id, new WoActionRequest(null, draft.RowVersion));

        Assert.Equal(WoStatus.Released, released.Status);
        Assert.Equal(before + 20, (await _pos.GetAsync(MockPoApi.SeedPoId)).InProductionQty);
        var snapshot = await _wos.GetTariffSnapshotAsync(draft.Id);
        Assert.Equal(["CUT", "BRD", "SEW", "QC"], snapshot.Steps.Select(s => s.ProcessCode));
        Assert.Equal(12, snapshot.Rates.Count); // 3 langkah internal × 4 size
        Assert.Equal(2, (await _wos.GetStatusHistoryAsync(draft.Id)).Count);
        var other = await NewDraft(5);
        await Fails(() => _wos.GetTariffSnapshotAsync(other.Id)); // Draft belum punya snapshot
    }

    [Fact]
    public async Task Output_mengubah_status_ke_In_Progress_progres_dan_batas_minimum_Qty()
    {
        var wo = await _wos.GetAsync(MockWoApi.SeedWoId);
        var line = wo.Lines[0];
        _wos.AddOutput(wo.Id, 1, line.PoLineId, 30);

        var progress = await _wos.GetProgressAsync(wo.Id);
        Assert.Equal(WoStatus.InProgress, (await _wos.GetAsync(wo.Id)).Status);
        Assert.Equal(30, progress.Steps[0].Cells[0].Output);
        Assert.Equal(StepState.InProgress, progress.Steps[0].State);
        Assert.Equal(StepState.NotStarted, progress.Steps[2].State);

        var current = await _wos.GetAsync(wo.Id);
        var below = await Fails(() => _wos.UpdateQuantitiesAsync(wo.Id, new UpdateWoQuantitiesRequest(current.Lines.Select(l => new SaveWoLine(l.PoLineId, l.PoLineId == line.PoLineId ? 20 : l.Qty)).ToList(), current.RowVersion)));
        Assert.Equal(ProblemCodes.WoQtyBelowOutput, below.Problem.Code);
        Assert.Contains("Qty minimal 30", below.Problem.Violations![0].Message);
    }

    [Fact]
    public async Task Ubah_Qty_menyesuaikan_Dalam_Produksi_PO_dan_menolak_di_atas_Sisa_Rencana()
    {
        var wo = await _wos.GetAsync(MockWoApi.SeedWoId);
        var line = wo.Lines[0]; // Qty 50, sisa PO 50 (100 - 50)
        UpdateWoQuantitiesRequest Req(int qty) => new(wo.Lines.Select(l => new SaveWoLine(l.PoLineId, l.PoLineId == line.PoLineId ? qty : l.Qty)).ToList(), wo.RowVersion);

        var over = await Fails(() => _wos.UpdateQuantitiesAsync(wo.Id, Req(101)));
        Assert.Equal(ProblemCodes.PoRemainingPlanExceeded, over.Problem.Code);
        Assert.Equal(100m, over.Problem.Violations![0].Limit);

        var updated = await _wos.UpdateQuantitiesAsync(wo.Id, Req(80));
        Assert.Equal(80, updated.Lines[0].Qty);
        Assert.Equal(20, (await _pos.GetAsync(MockPoApi.SeedPoId)).Lines[0].RemainingPlanQty);
        await Fails(() => _wos.UpdateQuantitiesAsync(wo.Id, Req(60))); // versi basi
    }

    [Fact]
    public async Task Alur_Selesai_Resume_Close_Reopen_dengan_alasan_dan_pengaruh_ke_PO()
    {
        var wo = await _wos.GetAsync(MockWoApi.SeedWoId);
        _wos.AddOutput(wo.Id, 4, wo.Lines[0].PoLineId, 10);
        wo = await _wos.GetAsync(wo.Id);

        var completed = await _wos.CompleteAsync(wo.Id, new WoActionRequest(null, wo.RowVersion));
        Assert.Equal(new[] { WoActions.Resume, WoActions.Close, WoActions.HandoverFg }, completed.AllowedActions);
        Assert.Contains("reason", (await Fails(() => _wos.ResumeAsync(wo.Id, new WoActionRequest(" ", completed.RowVersion)))).Problem.Errors!.Keys);
        var resumed = await _wos.ResumeAsync(wo.Id, new WoActionRequest("masih ada sisa", completed.RowVersion));
        completed = await _wos.CompleteAsync(wo.Id, new WoActionRequest(null, resumed.RowVersion));

        var closePreview = await _wos.PreviewCloseAsync(wo.Id);
        Assert.Equal(140, closePreview.ReturnedToPoPlanQty);
        Assert.Equal(10, Assert.Single(closePreview.UnhandedCells).Qty);
        var closed = await _wos.CloseAsync(wo.Id, new WoActionRequest(null, completed.RowVersion));
        Assert.Equal(0, (await _pos.GetAsync(MockPoApi.SeedPoId)).InProductionQty);

        var reopened = await _wos.ReopenAsync(wo.Id, new WoActionRequest("salah close", closed.RowVersion));
        Assert.Equal(WoStatus.Completed, reopened.Status);
        Assert.Equal(150, (await _pos.GetAsync(MockPoApi.SeedPoId)).InProductionQty);
    }

    [Fact]
    public async Task Batalkan_butuh_alasan_dan_ditolak_bila_ada_aktivitas_dan_melepas_Dalam_Produksi()
    {
        var wo = await _wos.GetAsync(MockWoApi.SeedWoId);
        Assert.Contains("reason", (await Fails(() => _wos.CancelAsync(wo.Id, new WoActionRequest("", wo.RowVersion)))).Problem.Errors!.Keys);
        _wos.AddOutput(wo.Id, 1, wo.Lines[0].PoLineId, 5);
        var active = await _wos.GetAsync(wo.Id);
        Assert.Equal(ProblemTypes.StateConflict, (await Fails(() => _wos.CancelAsync(wo.Id, new WoActionRequest("x", active.RowVersion)))).Problem.Type); // sudah In Progress

        var draft = await NewDraft(10);
        var released = await _wos.ReleaseAsync(draft.Id, new WoActionRequest(null, draft.RowVersion));
        var before = (await _pos.GetAsync(MockPoApi.SeedPoId)).InProductionQty;
        var cancelled = await _wos.CancelAsync(draft.Id, new WoActionRequest("batal", released.RowVersion));
        Assert.Equal((WoStatus.Cancelled, "batal"), (cancelled.Status, cancelled.CancelReason));
        Assert.Equal(before - 10, (await _pos.GetAsync(MockPoApi.SeedPoId)).InProductionQty);
    }

    [Fact]
    public async Task Daftar_menyaring_status_pencarian_dan_menandai_terlambat()
    {
        var late = await NewDraft(5);
        await _wos.UpdateScheduleAsync(late.Id, new UpdateWoScheduleRequest(null, new DateOnly(2020, 1, 1), null, late.RowVersion));
        var released = await _wos.ReleaseAsync(late.Id, new WoActionRequest(null, (await _wos.GetAsync(late.Id)).RowVersion));

        Assert.Equal(2, (await _wos.ListAsync(new WoListQuery())).TotalCount);
        Assert.Equal([released.Code], (await _wos.ListAsync(new WoListQuery { Terlambat = true })).Items.Select(i => i.Code));
    }
}

public sealed class WoPageTests : WebTestBase
{
    private static readonly string[] AllWo = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("WorkOrder.", StringComparison.Ordinal) || p.Key.StartsWith("CustomerPO.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly MockWoApi _wos;
    private readonly MockPoApi _pos;

    public WoPageTests()
    {
        var sizes = new MockSizeApi();
        var customers = new MockCustomerApi();
        var fgs = new MockFgApi(sizes, new MockProcessApi(), customers);
        _pos = new MockPoApi(customers, fgs, new MockColorApi(), sizes);
        _wos = new MockWoApi(_pos, fgs, customers);
        Services.AddSingleton<IWorkOrdersApi>(_wos);
        Services.AddSingleton<IPurchaseOrdersApi>(_pos);
        SignIn(AllWo);
    }

    private static IElement DialogConfirm<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelectorAll("fluent-button").Last();

    [Fact]
    public void Daftar_menampilkan_WO_status_progres_dan_chip_filter()
    {
        var cut = Render<WoListPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        Assert.Contains("Released", cut.Find("tbody").TextContent);
        Assert.Contains("PO-202611-0001", cut.Find("tbody").TextContent);
        Assert.Equal("false", cut.FindAll(".gms-chipbtn")[0].GetAttribute("aria-pressed"));
        cut.FindAll(".gms-chipbtn")[1].Click(); // Menunggu Close
        cut.WaitForAssertion(() => Assert.Contains("Tidak ada WO untuk filter ini", cut.Markup));
    }

    [Fact]
    public void Detail_Released_menampilkan_aksi_kontekstual_matriks_progres_dan_tab_Tarif_dengan_izin()
    {
        var cut = Render<WoDetailPage>(p => p.Add(x => x.Id, MockWoApi.SeedWoId));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-progress-matrix")));
        var actions = cut.Find(".gms-pageheader-actions").TextContent;
        Assert.Contains("Ubah Qty", actions);
        Assert.Contains("Batalkan", actions);
        Assert.DoesNotContain("Tandai Selesai", actions);
        Assert.Equal(["Progres", "Baris", "Tarif", "Riwayat"], cut.FindAll("[role=tab]").Select(t => t.TextContent));
        Assert.Equal(4, cut.FindAll(".gms-progress-matrix tbody tr").Count);
        Assert.Contains("Belum ada output", cut.Markup);
        Assert.Contains("Terkirim 0 · Diterima 0 · Sisa di vendor 0", cut.Find(".gms-progress-matrix").TextContent);
    }

    [Fact]
    public void Tab_Tarif_disembunyikan_tanpa_WorkOrder_Tarif_View()
    {
        SignIn(Permissions.WorkOrder.View);
        var cut = Render<WoDetailPage>(p => p.Add(x => x.Id, MockWoApi.SeedWoId));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role=tab]")));
        Assert.Equal(["Progres", "Baris", "Riwayat"], cut.FindAll("[role=tab]").Select(t => t.TextContent));
        Assert.Empty(cut.FindAll(".gms-pageheader-actions *"));
    }

    [Fact]
    public void Matriks_progres_menandai_kelebihan_produksi_dan_label_sel_dibaca_lengkap()
    {
        var wo = Sync(_wos.GetAsync(MockWoApi.SeedWoId));
        _wos.AddOutput(wo.Id, 1, wo.Lines[0].PoLineId, 55); // Qty 50 → +5
        var cut = Render<WoDetailPage>(p => p.Add(x => x.Id, MockWoApi.SeedWoId));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".gms-progress-matrix")));
        Assert.Contains("+5", cut.Find(".gms-over").TextContent);
        Assert.Contains("Cutting, Navy S: 55 dari 50", cut.Markup);
        Assert.Contains("◔ Berjalan", cut.Find(".gms-progress-matrix tbody tr").TextContent);
    }

    [Fact]
    public void Ubah_Qty_menampilkan_pesan_server_bila_di_bawah_output_dan_menyimpan_bila_valid()
    {
        var wo = Sync(_wos.GetAsync(MockWoApi.SeedWoId));
        _wos.AddOutput(wo.Id, 1, wo.Lines[0].PoLineId, 30);
        var cut = Render<WoDetailPage>(p => p.Add(x => x.Id, MockWoApi.SeedWoId));
        cut.WaitForAssertion(() => Assert.Contains("Ubah Qty", cut.Find(".gms-pageheader-actions").TextContent));

        cut.FindAll(".gms-pageheader-actions fluent-button").Single(b => b.TextContent.Contains("Ubah Qty")).Click();
        cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelectorAll("input")[0].Input("20");
        DialogConfirm(cut).Click();
        cut.WaitForAssertion(() => Assert.Contains("Qty minimal 30", cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent));

        cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelectorAll("input")[0].Input("60");
        DialogConfirm(cut).Click();
        cut.WaitForAssertion(() => Assert.Equal(60, Sync(_wos.GetAsync(MockWoApi.SeedWoId)).Lines[0].Qty));
        Assert.Equal(60, Sync(_wos.GetAsync(MockWoApi.SeedWoId)).Lines[0].Qty);
    }

    [Fact]
    public void Tandai_Selesai_dan_Buka_Kembali_dengan_alasan_wajib()
    {
        var wo = Sync(_wos.GetAsync(MockWoApi.SeedWoId));
        _wos.AddOutput(wo.Id, 1, wo.Lines[0].PoLineId, 10);
        var cut = Render<WoDetailPage>(p => p.Add(x => x.Id, MockWoApi.SeedWoId));
        cut.WaitForAssertion(() => Assert.Contains("Tandai Selesai", cut.Find(".gms-pageheader-actions").TextContent));

        cut.FindAll(".gms-pageheader-actions fluent-button").Single(b => b.TextContent.Contains("Tandai Selesai")).Click();
        DialogConfirm(cut).Click();
        cut.WaitForAssertion(() => Assert.Equal("Completed", cut.Find(".gms-pageheader .gms-chip").TextContent.Trim()));

        cut.FindAll(".gms-pageheader-actions fluent-button").Single(b => b.TextContent.Contains("Buka Kembali")).Click();
        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
        cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).QuerySelector("textarea")!.Input("masih ada sisa");
        DialogConfirm(cut).Click();
        cut.WaitForAssertion(() => Assert.Equal("In Progress", cut.Find(".gms-pageheader .gms-chip").TextContent.Trim()));
    }

    [Fact]
    public void Close_menampilkan_pratinjau_sisa_kembali_ke_PO_dan_peringatan_belum_diserahterimakan()
    {
        var wo = Sync(_wos.GetAsync(MockWoApi.SeedWoId));
        _wos.AddOutput(wo.Id, 4, wo.Lines[0].PoLineId, 10);
        Sync(_wos.CompleteAsync(wo.Id, new WoActionRequest(null, Sync(_wos.GetAsync(wo.Id)).RowVersion)));
        var cut = Render<WoDetailPage>(p => p.Add(x => x.Id, MockWoApi.SeedWoId));
        cut.WaitForAssertion(() => Assert.Contains("Close", cut.Find(".gms-pageheader-actions").TextContent));

        cut.FindAll(".gms-pageheader-actions fluent-button").Single(b => b.TextContent == "Close").Click();

        var dialog = cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null).TextContent;
        Assert.Contains("kembali ke Sisa Rencana PO", dialog);
        Assert.Contains("Output belum diserahterimakan", dialog);
        DialogConfirm(cut).Click();
        cut.WaitForAssertion(() => Assert.Equal("Closed", cut.Find(".gms-pageheader .gms-chip").TextContent.Trim()));
    }

    [Fact]
    public void Rilis_Draft_menampilkan_daftar_periksa_server_dan_merilis()
    {
        var po = Sync(_pos.GetAsync(MockPoApi.SeedPoId));
        var draft = Sync(_wos.CreateAsync(new SaveWoRequest(po.Id, MockFgApi.KemejaId, null, null, null, [new SaveWoLine(po.Lines[2].Id, 20)])));
        var cut = Render<WoDetailPage>(p => p.Add(x => x.Id, draft.Id));
        cut.WaitForAssertion(() => Assert.Contains("Rilis", cut.Find(".gms-pageheader-actions").TextContent));

        cut.FindAll(".gms-pageheader-actions fluent-button").Single(b => b.TextContent == "Rilis").Click();

        var dialog = cut.FindAll("dialog").Last(d => d.QuerySelector("h2") is not null);
        Assert.Contains("Terpenuhi:", dialog.TextContent);
        Assert.Contains("4 langkah routing dan 12 sel tarif", dialog.TextContent);
        Assert.Contains("dibekukan", dialog.TextContent);
        DialogConfirm(cut).Click();
        cut.WaitForAssertion(() => Assert.Equal("Released", cut.Find(".gms-pageheader .gms-chip").TextContent.Trim()));
    }

    [Fact]
    public void Form_buat_WO_dari_PO_memuat_grid_dengan_batas_sisa_dan_menyimpan_Draft()
    {
        var cut = Render<WoFormPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));
        cut.FindAll("form select")[0].Change(MockPoApi.SeedPoId.ToString());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("form select").Count));
        cut.FindAll("form select")[1].Change(MockFgApi.KemejaId.ToString());

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));
        Assert.Contains("maks 100", cut.Markup); // Sisa Rencana dari server (baris S setelah WO contoh = 50; M = 100 → maks tertinggi 150)
        cut.Find("input[aria-label='Navy, Size L']").Input("20");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Matches("/work-orders/[0-9a-f-]{36}$", Nav.Uri));
        Assert.Equal(2, Sync(_wos.ListAsync(new WoListQuery())).TotalCount);
    }

    [Fact]
    public void Form_tanpa_Qty_menampilkan_galat_dan_tidak_menyimpan()
    {
        var cut = Render<WoFormPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));
        cut.FindAll("form select")[0].Change(MockPoApi.SeedPoId.ToString());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("form select").Count));
        cut.FindAll("form select")[1].Change(MockFgApi.KemejaId.ToString());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-matrix")));

        cut.Find("form").Submit();

        Assert.Contains("Minimal satu Qty terisi", cut.Find(".gms-formsummary").TextContent);
    }
}
