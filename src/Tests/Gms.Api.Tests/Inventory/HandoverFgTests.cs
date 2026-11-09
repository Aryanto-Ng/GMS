using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Inventory;

/// <summary>Serah terima FG: batas, alokasi otomatis ke PO, void/koreksi, dan konkurensi (FSD Serah Terima & Stok FG 4.1, 4.2, AC-ST-31).</summary>
[Collection(ApiCollection.Name)]
public sealed class HandoverFgTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    // ===== Konteks =====

    [Fact]
    public async Task Konteks_menampilkan_batas_per_sel_dan_diblok_untuk_WO_Released_atau_Closed()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60, woQtyMedium: 40);
        string Url() => $"/api/v1/handovers/fg/context?woId={s.Wo.Id}";

        var released = (await H.Api.GetAsync(Url(), H.AdminToken)).Ok().As<HandoverFgContextDto>();
        Assert.Contains("output", released.BlockedReason);

        await H.ProduceAsync(s, 50, 20);
        await H.HandoverAsync(s, (s.LineS, 15));

        var ctx = (await H.Api.GetAsync(Url(), H.AdminToken)).Ok().As<HandoverFgContextDto>();
        Assert.Null(ctx.BlockedReason);
        Assert.Equal((s.Wo.Code, s.Po.Code), (ctx.WoCode, ctx.PoCode));
        var cellS = ctx.Columns.Single(c => c.WoLineId == s.LineS);
        Assert.Equal((35, 15, 50, 60), (cellS.MaxHandover, cellS.HandedOver, cellS.LastStepOutput, cellS.WoQty));
        Assert.Equal(20, ctx.Columns.Single(c => c.WoLineId == s.LineM).MaxHandover);

        await H.SetWoStatusAsync(s.Wo.Id, WoStatus.Closed);
        Assert.Contains("ditutup", (await H.Api.GetAsync(Url(), H.AdminToken)).Ok().As<HandoverFgContextDto>().BlockedReason);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/handovers/fg/context?woId={Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    // ===== Simpan =====

    [Fact]
    public async Task Serah_terima_menambah_stok_mengalokasikan_otomatis_ke_PO_dan_kelebihan_menjadi_stok_bebas()
    {
        var s = await H.NewProdSetupAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);
        await H.ProduceAsync(s, 70, 40); // S berlebih 10 di atas Qty WO

        var result = await H.HandoverAsync(s, (s.LineS, 70), (s.LineM, 40));

        Assert.Equal((100, 10, s.Po.Code), (result.AutoAllocatedQty, result.FreeStockQty, result.PoCode));
        var fg = s.World.Fg.Fg.Id;
        Assert.Equal((70, 60), await H.StockAsync(fg, s.World.Color.Id, s.World.Small.Id));
        Assert.Equal((40, 40), await H.StockAsync(fg, s.World.Color.Id, s.World.Medium.Id));
        Assert.Equal((60, 0, 0), await H.PoLineAsync(s, s.Po.PoLineId(s.World.Small)));

        var detail = await H.GetHandoverAsync(result.Id);
        Assert.Equal(110, detail.Item.TotalQty);
        Assert.Equal(60, detail.Lines.Single(l => l.WoLineId == s.LineS).AutoAllocatedQty);
        Assert.Equal(WoStatus.InProgress, (await H.GetWoAsync(s.Wo.Id)).Status);

        var allocations = (await H.Api.GetAsync($"/api/v1/purchase-orders/{s.Po.Id}/allocations", H.AdminToken)).Ok().As<List<AllocationEntryDto>>();
        Assert.All(allocations, a => Assert.Equal(AllocationSource.Handover, a.Source));
        Assert.Equal(100, allocations.Sum(a => a.Qty));

        var progress = (await H.Api.GetAsync($"/api/v1/work-orders/{s.Wo.Id}/progress", H.AdminToken)).Ok().As<WoProgressDto>();
        Assert.Equal((110, 0), (progress.HandedOverTotal, progress.UnhandedTotal));

        await H.AssertStockConsistentAsync(fg, s.World.Color.Id, s.World.Small.Id);
        await H.AssertStockConsistentAsync(fg, s.World.Color.Id, s.World.Medium.Id);
        await H.AssertInProductionInvariantAsync(s.Po.Id);

        await using var db = H.CreateDbContext();
        Assert.Contains("HandoverFgCreated", await db.AuditLogs.Where(a => a.TargetId == result.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Serah_terima_bertahap_alokasi_otomatis_berhenti_di_Qty_WO()
    {
        var s = await H.NewProdSetupAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);
        await H.ProduceAsync(s, 70);

        var first = await H.HandoverAsync(s, (s.LineS, 40));
        var second = await H.HandoverAsync(s, (s.LineS, 30));

        Assert.Equal((40, 0), (first.AutoAllocatedQty, first.FreeStockQty));
        Assert.Equal((20, 10), (second.AutoAllocatedQty, second.FreeStockQty)); // 60 − 40 = 20 sisa Qty WO
        Assert.Equal((70, 60), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
        await H.AssertInProductionInvariantAsync(s.Po.Id);
    }

    [Fact]
    public async Task Qty_melebihi_output_langkah_terakhir_ditolak_dengan_batas_dan_validasi_lain()
    {
        var s = await H.NewProdSetupAsync();
        await H.ProduceAsync(s, 30);
        await H.HandoverAsync(s, (s.LineS, 10));

        var over = await H.PostHandoverAsync(s, [(s.LineS, 21)]);
        var problem = ProblemAssert.Is(over, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.HandoverExceedsOutput, problem.Code);
        Assert.Equal((21m, 20m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));
        Assert.Equal((10, 10), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));

        Assert.Contains("cells", ProblemAssert.Is(await H.PostHandoverAsync(s, [(s.LineS, 0)]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("cells[0].qty", ProblemAssert.Is(await H.PostHandoverAsync(s, [(s.LineS, -1)]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("date", ProblemAssert.Is(await H.PostHandoverAsync(s, [(s.LineS, 1)], date: Today(1)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("docNo", ProblemAssert.Is(await H.PostHandoverAsync(s, [(s.LineS, 1)], docNo: new string('x', 101)), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("cells[0].woLineId", ProblemAssert.Is(await H.PostHandoverAsync(s, [(Guid.NewGuid(), 1)]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Fact]
    public async Task Serah_terima_ditolak_pada_WO_Released_Closed_dan_diizinkan_pada_Completed()
    {
        var s = await H.NewProdSetupAsync();
        ProblemAssert.Is(await H.PostHandoverAsync(s, [(s.LineS, 1)]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        await H.ProduceAsync(s, 30);
        await H.WoDoAsync(s.Wo.Id, "complete");
        (await H.PostHandoverAsync(s, [(s.LineS, 10)])).Ok("Completed tetap boleh serah terima");
        Assert.Equal(WoStatus.Completed, (await H.GetWoAsync(s.Wo.Id)).Status); // AC-ST-06

        await H.SetWoStatusAsync(s.Wo.Id, WoStatus.Closed);
        var closed = ProblemAssert.Is(await H.PostHandoverAsync(s, [(s.LineS, 1)]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        Assert.Contains("Reopen", closed.Detail);
    }

    [Fact]
    public async Task PO_yang_sudah_Completed_membuat_seluruh_qty_menjadi_stok_bebas()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        await H.ProduceAsync(s, 30);
        await using (var db = H.CreateDbContext())
        {
            await db.PurchaseOrders.Where(p => p.Id == s.Po.Id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, PoStatus.Completed));
        }

        var result = await H.HandoverAsync(s, (s.LineS, 30));

        Assert.Equal((0, 30), (result.AutoAllocatedQty, result.FreeStockQty));
        Assert.Equal((30, 0), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
        await H.AssertInProductionInvariantAsync(s.Po.Id);
    }

    [Fact]
    public async Task Pratinjau_memperkirakan_pembagian_dan_tidak_menyimpan_apa_pun()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60, woQtyMedium: 40);
        await H.ProduceAsync(s, 70, 40);
        var request = new SaveHandoverFgRequest(s.Wo.Id, Today(), null, null, null, Cells((s.LineS, 70), (s.LineM, 10)).ToList());

        var preview = (await H.Api.PostAsync("/api/v1/handovers/fg/preview", request, H.AdminToken)).Ok().As<HandoverFgPreviewDto>();

        Assert.Equal((70, 10, s.Po.Code), (preview.AutoAllocatedQty, preview.FreeStockQty, preview.PoCode));
        Assert.Equal((60, 10), (preview.Cells.Single(c => c.WoLineId == s.LineS).AutoAllocatedQty, preview.Cells.Single(c => c.WoLineId == s.LineS).FreeStockQty));
        Assert.Equal((0, 0), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        Assert.Equal(0, (await H.Api.GetAsync($"/api/v1/handovers/fg?woId={s.Wo.Id}", H.AdminToken)).Ok().As<Gms.Shared.Common.PagedResult<HandoverFgListItem>>().TotalCount);
        await H.AssertInProductionInvariantAsync(s.Po.Id);
    }

    // ===== Void =====

    [Fact]
    public async Task Void_melepas_alokasi_otomatis_membalik_stok_dan_mengembalikan_Dalam_Produksi()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        await H.ProduceAsync(s, 70);
        var result = await H.HandoverAsync(s, (s.LineS, 70));
        var line = s.Po.PoLineId(s.World.Small);

        Assert.Contains("reason", ProblemAssert.Is(await H.VoidHandoverAsync(result.Id, " "), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Equal(HttpStatusCode.NoContent, (await H.VoidHandoverAsync(result.Id, "salah hitung")).Status);

        Assert.Equal((0, 0), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        Assert.Equal((0, 0, 60), await H.PoLineAsync(s, line));
        var detail = await H.GetHandoverAsync(result.Id);
        Assert.Equal((DocStatus.Voided, "salah hitung", false), (detail.Item.Status, detail.VoidReason, detail.CanVoid));
        Assert.Equal(0, (await H.GetWoAsync(s.Wo.Id)).HandedOverQty);
        await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
        await H.AssertInProductionInvariantAsync(s.Po.Id);

        ProblemAssert.Is(await H.VoidHandoverAsync(result.Id), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        var ledger = (await H.Api.GetAsync($"/api/v1/stock/ledger?fgId={s.World.Fg.Fg.Id}&colorId={s.World.Color.Id}&sizeId={s.World.Small.Id}", H.AdminToken)).Ok().As<StockLedgerResult>();
        Assert.Equal([StockLedgerKind.HandoverReversal, StockLedgerKind.HandoverReceipt], ledger.Rows.Items.Select(r => r.Kind));
        Assert.Equal([0, 70], ledger.Rows.Items.Select(r => r.RunningBalance));
    }

    [Fact]
    public async Task Void_ditolak_bila_stok_bebas_sudah_dialokasikan_ke_PO_lain_dan_pratinjau_menjelaskan()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        await H.ProduceAsync(s, 70);
        var result = await H.HandoverAsync(s, (s.LineS, 70)); // 60 teralokasi, 10 bebas
        var other = await H.NewPoAsync(s.World, [s.World.Line(s.World.Small, 50)]);
        (await H.Api.PostAsync($"/api/v1/po-lines/{other.PoLineId(s.World.Small)}/allocate", new AllocateRequest(10, null), H.AdminToken)).Ok();

        var preview = (await H.Api.PostAsync($"/api/v1/handovers/fg/{result.Id}/void/preview", new { }, H.AdminToken)).Ok().As<ImpactPreview>();
        Assert.False(preview.CanProceed);
        Assert.Contains(preview.Rows, r => r is { Metric: "Stok Tersedia", Ok: false, After: -10 });
        Assert.Contains("dialokasikan ke PO lain", preview.Violations[0].Message);

        var response = await H.VoidHandoverAsync(result.Id);
        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.StockInsufficient, problem.Code);
        Assert.Equal(DocStatus.Active, (await H.GetHandoverAsync(result.Id)).Item.Status);
        Assert.Equal((70, 70), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
    }

    [Fact]
    public async Task Void_ditolak_bila_stok_sudah_dikirim_ke_customer()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        await H.ProduceAsync(s, 30);
        var result = await H.HandoverAsync(s, (s.LineS, 30));
        var line = s.Po.PoLineId(s.World.Small);
        // Simulasi pengiriman (modul Pengiriman belum ada): alokasi dikonsumsi, stok fisik berkurang, terkirim bertambah.
        await using (var db = H.CreateDbContext())
        {
            await db.PoLines.Where(l => l.Id == line).ExecuteUpdateAsync(l => l.SetProperty(x => x.AllocatedQty, 0).SetProperty(x => x.DeliveredQty, 30));
            await db.StockBalances.Where(x => x.FgId == s.World.Fg.Fg.Id && x.SizeId == s.World.Small.Id).ExecuteUpdateAsync(x => x.SetProperty(b => b.OnHand, 0).SetProperty(b => b.Allocated, 0));
        }

        var problem = ProblemAssert.Is(await H.VoidHandoverAsync(result.Id), HttpStatusCode.Conflict, ProblemTypes.RuleViolation);

        Assert.Equal(ProblemCodes.StockInsufficient, problem.Code);
        Assert.Contains("dikirim", problem.Violations![0].Message);
        Assert.Equal(DocStatus.Active, (await H.GetHandoverAsync(result.Id)).Item.Status);
    }

    [Fact]
    public async Task Void_ditolak_pada_WO_Closed_dan_disembunyikan_dari_aksi()
    {
        var s = await H.NewProdSetupAsync();
        await H.ProduceAsync(s, 30);
        var result = await H.HandoverAsync(s, (s.LineS, 10));
        await H.SetWoStatusAsync(s.Wo.Id, WoStatus.Closed);

        var detail = await H.GetHandoverAsync(result.Id);

        Assert.False(detail.CanVoid);
        Assert.False(detail.CanCorrect);
        Assert.Contains("ditutup", detail.DisabledReason);
        ProblemAssert.Is(await H.VoidHandoverAsync(result.Id), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Koreksi =====

    [Fact]
    public async Task Koreksi_atomik_lolos_walau_void_biasa_ditolak_karena_kondisi_akhir_valid()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        await H.ProduceAsync(s, 70);
        var result = await H.HandoverAsync(s, (s.LineS, 70)); // 60 teralokasi + 10 bebas
        var other = await H.NewPoAsync(s.World, [s.World.Line(s.World.Small, 50)]);
        (await H.Api.PostAsync($"/api/v1/po-lines/{other.PoLineId(s.World.Small)}/allocate", new AllocateRequest(10, null), H.AdminToken)).Ok();
        ProblemAssert.Is(await H.VoidHandoverAsync(result.Id), HttpStatusCode.Conflict, ProblemTypes.RuleViolation);

        var correct = (await H.Api.PostAsync($"/api/v1/handovers/fg/{result.Id}/correct",
            new CorrectHandoverFgRequest("catat ulang", Today(), "DOC-2", "Budi", null, Cells((s.LineS, 70)).ToList()), H.AdminToken)).Ok().As<HandoverFgResultDto>();

        Assert.Equal((60, 10), (correct.AutoAllocatedQty, correct.FreeStockQty));
        var old = await H.GetHandoverAsync(result.Id);
        var replacement = await H.GetHandoverAsync(correct.Id);
        Assert.Equal((DocStatus.Voided, correct.Id), (old.Item.Status, old.ReplacedById!.Value));
        Assert.Equal((result.Id, "DOC-2"), (replacement.ReplacesId!.Value, replacement.Item.DocNo));
        Assert.Equal((70, 70), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
        await H.AssertInProductionInvariantAsync(s.Po.Id);
    }

    [Fact]
    public async Task Koreksi_ditolak_bila_kondisi_akhir_melanggar_batas_output_atau_stok_dan_validasi_alasan()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        await H.ProduceAsync(s, 30);
        var result = await H.HandoverAsync(s, (s.LineS, 30));
        string Url(string a) => $"/api/v1/handovers/fg/{result.Id}/{a}";

        var tooMuch = await H.Api.PostAsync(Url("correct"), new CorrectHandoverFgRequest("x", Today(), null, null, null, Cells((s.LineS, 31)).ToList()), H.AdminToken);
        Assert.Equal(ProblemCodes.HandoverExceedsOutput, ProblemAssert.Is(tooMuch, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        Assert.Contains("reason", ProblemAssert.Is(await H.Api.PostAsync(Url("correct"), new CorrectHandoverFgRequest(" ", Today(), null, null, null, Cells((s.LineS, 5)).ToList()), H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("cells", ProblemAssert.Is(await H.Api.PostAsync(Url("correct"), new CorrectHandoverFgRequest("x", Today(), null, null, null, Cells((s.LineS, 0)).ToList()), H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var preview = (await H.Api.PostAsync(Url("correct/preview"), new CorrectHandoverFgRequest("x", Today(), null, null, null, Cells((s.LineS, 5)).ToList()), H.AdminToken)).Ok().As<ImpactPreview>();
        Assert.True(preview.CanProceed);
        var badPreview = (await H.Api.PostAsync(Url("correct/preview"), new CorrectHandoverFgRequest("x", Today(), null, null, null, Cells((s.LineS, 31)).ToList()), H.AdminToken)).Ok().As<ImpactPreview>();
        Assert.False(badPreview.CanProceed); // pratinjau menjelaskan pelanggaran, bukan gagal
        Assert.Equal(ProblemCodes.HandoverExceedsOutput, badPreview.Violations[0].Code);
        Assert.Equal((30, 30), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id)); // pratinjau tidak mengubah apa pun

        await H.VoidHandoverAsync(result.Id);
        ProblemAssert.Is(await H.Api.PostAsync(Url("correct"), new CorrectHandoverFgRequest("x", Today(), null, null, null, Cells((s.LineS, 5)).ToList()), H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Integrasi dengan Production Output (E5) =====

    [Fact]
    public async Task Void_output_langkah_terakhir_ditolak_bila_akan_lebih_kecil_dari_yang_sudah_diserahterimakan()
    {
        var s = await H.NewProdSetupAsync();
        var worker = await H.NewWorkerAsync();
        var vendor = await H.NewVendorAsync();
        await H.OutAsync(s, s.Cut, worker.Id, (s.LineS, 30));
        await H.SendAsync(s, vendor.Id, (s.LineS, 30));
        await H.ReceiveAsync(s, vendor.Id, (s.LineS, 30));
        var sew = await H.OutAsync(s, s.Sew, worker.Id, (s.LineS, 30));
        await H.HandoverAsync(s, (s.LineS, 20));

        var response = await H.VoidAsync(sew);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.OutputBelowDownstream, problem.Code);
        Assert.Contains("diserahterimakan", problem.Violations![0].Message);
        Assert.Equal((30, 0), await H.BalanceAsync(s.LineS, s.Sew));
    }

    // ===== Daftar dan permission =====

    [Fact]
    public async Task Daftar_menyaring_dan_permission_dipisah_untuk_lihat_buat_void_dan_koreksi()
    {
        var s = await H.NewProdSetupAsync();
        await H.ProduceAsync(s, 30);
        var a = await H.HandoverAsync(s, (s.LineS, 10));
        var b = await H.HandoverAsync(s, (s.LineS, 5));
        await H.VoidHandoverAsync(b.Id);
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.SerahTerimaFG.View)).Id);
        var creator = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.SerahTerimaFG.View, Permissions.SerahTerimaFG.Create)).Id);
        var none = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);

        async Task<Gms.Shared.Common.PagedResult<HandoverFgListItem>> List(string query) =>
            (await H.Api.GetAsync($"/api/v1/handovers/fg?pageSize=50&woId={s.Wo.Id}{query}", H.AdminToken)).Ok().As<Gms.Shared.Common.PagedResult<HandoverFgListItem>>();

        Assert.Equal(2, (await List("")).TotalCount);
        Assert.Equal([b.Id], (await List("&status=Voided")).Items.Select(i => i.Id));
        Assert.Equal(2, (await List($"&poId={s.Po.Id}&fgId={s.World.Fg.Fg.Id}")).TotalCount);
        Assert.Equal(0, (await List($"&from={Today(1)}")).TotalCount);
        Assert.Equal(2, (await List($"&q={s.Wo.Code}")).TotalCount);
        var first = (await List("")).Items.Single(i => i.Id == a.Id);
        Assert.Equal((s.Wo.Code, s.Po.Code, s.World.Fg.Fg.Code, 10), (first.WoCode, first.PoCode, first.FgCode, first.TotalQty));

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/handovers/fg", none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.PostHandoverAsync(s, [(s.LineS, 1)], token: viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.PostHandoverAsync(s, [(s.LineS, 1)], token: creator.Token)).IsSuccess);
        ProblemAssert.Is(await H.VoidHandoverAsync(a.Id, token: creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/handovers/fg/{a.Id}/correct", new CorrectHandoverFgRequest("x", Today(), null, null, null, Cells((s.LineS, 5)).ToList()), creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/handovers/fg/{Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    // ===== KONKURENSI (AC-ST-31) =====

    [Fact]
    public async Task Dua_serah_terima_bersamaan_yang_bersama_sama_melebihi_output_tepat_satu_lolos()
    {
        for (var round = 0; round < 5; round++)
        {
            var s = await H.NewProdSetupAsync(woQtySmall: 60);
            await H.ProduceAsync(s, 30);
            var gate = new TaskCompletionSource();

            Task<ApiResponse> Go() => Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostHandoverAsync(s, [(s.LineS, 20)]);
            });

            var tasks = new[] { Go(), Go() };
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            Assert.Equal(ProblemCodes.HandoverExceedsOutput, ProblemAssert.Is(results.Single(r => !r.IsSuccess), HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
            Assert.Equal((20, 20), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
            await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
            await H.AssertInProductionInvariantAsync(s.Po.Id);
        }
    }

    [Fact]
    public async Task Dua_puluh_serah_terima_paralel_pada_output_10_tepat_10_lolos_tanpa_5xx()
    {
        var s = await H.NewProdSetupAsync(woQtySmall: 60);
        await H.ProduceAsync(s, 10);
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await H.PostHandoverAsync(s, [(s.LineS, 1)]);
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, results.Count(r => r.IsSuccess));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        Assert.Equal((10, 10), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
        await H.AssertInProductionInvariantAsync(s.Po.Id);
    }

    [Fact]
    public async Task Void_bersamaan_dengan_alokasi_manual_stok_bebas_tidak_pernah_membuat_stok_tersedia_negatif()
    {
        for (var round = 0; round < 5; round++)
        {
            var s = await H.NewProdSetupAsync(woQtySmall: 60);
            await H.ProduceAsync(s, 70);
            var result = await H.HandoverAsync(s, (s.LineS, 70)); // 60 teralokasi + 10 bebas
            var other = await H.NewPoAsync(s.World, [s.World.Line(s.World.Small, 50)]);
            var gate = new TaskCompletionSource();

            var voiding = Task.Run(async () =>
            {
                await gate.Task;
                return await H.VoidHandoverAsync(result.Id);
            });
            var allocating = Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync($"/api/v1/po-lines/{other.PoLineId(s.World.Small)}/allocate", new AllocateRequest(10, null), H.AdminToken);
            });
            gate.SetResult();
            var results = await Task.WhenAll(voiding, allocating);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            Assert.True(results.Count(r => r.IsSuccess) >= 1);
            await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
            await H.AssertInProductionInvariantAsync(s.Po.Id);
        }
    }

    [Fact]
    public async Task Serah_terima_bersamaan_dengan_void_output_langkah_terakhir_tidak_pernah_merusak_invarian()
    {
        for (var round = 0; round < 4; round++)
        {
            var s = await H.NewProdSetupAsync(woQtySmall: 60);
            var worker = await H.NewWorkerAsync();
            var vendor = await H.NewVendorAsync();
            await H.OutAsync(s, s.Cut, worker.Id, (s.LineS, 20));
            await H.SendAsync(s, vendor.Id, (s.LineS, 20));
            await H.ReceiveAsync(s, vendor.Id, (s.LineS, 20));
            var sew = await H.OutAsync(s, s.Sew, worker.Id, (s.LineS, 20));
            var gate = new TaskCompletionSource();

            var hand = Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostHandoverAsync(s, [(s.LineS, 15)]);
            });
            var voidOut = Task.Run(async () =>
            {
                await gate.Task;
                return await H.VoidAsync(sew);
            });
            gate.SetResult();
            var results = await Task.WhenAll(hand, voidOut);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            await H.AssertLedgerInvariantAsync(s);
            await using var db = H.CreateDbContext();
            var handed = await db.WoLines.Where(l => l.Id == s.LineS).Select(l => l.HandedOverQty).SingleAsync();
            var lastOutput = (await H.BalanceAsync(s.LineS, s.Sew)).Qty;
            Assert.True(lastOutput >= handed, $"output {lastOutput} < diserahterimakan {handed}");
        }
    }
}
