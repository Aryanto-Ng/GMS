using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.WorkOrders;

/// <summary>Pembuatan dan perubahan WO Draft (FSD Work Order 4.1, 4.2).</summary>
[Collection(ApiCollection.Name)]
public sealed class WoCreateAndDraftTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    [Fact]
    public async Task Membuat_WO_Draft_menghasilkan_kode_label_riwayat_audit_dan_menambah_Dalam_Produksi()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 50)]);

        var wo = await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 60), (po.PoLineId(w.Medium), 50));

        Assert.Matches(@"^WO-\d{6}-\d{4}$", wo.Code);
        Assert.Equal((WoStatus.Draft, 110, po.Id, w.Fg.Fg.Code), (wo.Status, wo.TotalQty, wo.PoId, wo.FgCode));
        Assert.Equal(2, wo.Lines.Count);
        Assert.All(wo.Lines, l => Assert.False(string.IsNullOrEmpty(l.ColorSize.ColorCode)));
        Assert.Contains(WoActions.Release, wo.AllowedActions);

        var afterPo = await H.GetPoAsync(po.Id);
        Assert.Equal(60, afterPo.Lines.Single(l => l.Id == po.PoLineId(w.Small)).InProductionQty);
        Assert.Equal(40, afterPo.Lines.Single(l => l.Id == po.PoLineId(w.Small)).RemainingPlanQty);
        Assert.Equal(0, afterPo.Lines.Single(l => l.Id == po.PoLineId(w.Medium)).RemainingPlanQty);
        await H.AssertInProductionInvariantAsync(po.Id);

        var history = (await H.Api.GetAsync($"/api/v1/work-orders/{wo.Id}/status-history", H.AdminToken)).Ok().As<List<WoStatusHistoryItem>>();
        var first = Assert.Single(history);
        Assert.Equal((null, WoStatus.Draft), (first.FromStatus, first.ToStatus));

        await using var db = H.CreateDbContext();
        Assert.Contains("WoCreated", await db.AuditLogs.Where(a => a.TargetId == wo.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Kode_WO_berurutan_dan_unik()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);

        var a = await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 10));
        var b = await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 10));

        Assert.NotEqual(a.Code, b.Code);
        Assert.True(string.CompareOrdinal(a.Code, b.Code) < 0);
    }

    [Fact]
    public async Task Sel_ber_Qty_nol_tidak_disimpan_dan_semua_nol_ditolak()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 100)]);

        var wo = await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 30), (po.PoLineId(w.Medium), 0));
        Assert.Single(wo.Lines);

        var allZero = await H.PostWoAsync(w.WoRequest(po, [(po.PoLineId(w.Small), 0), (po.PoLineId(w.Medium), 0)]));
        Assert.Contains("lines", ProblemAssert.Is(allZero, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Fact]
    public async Task Validasi_header_qty_duplikat_dan_baris_asing()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var otherPo = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var line = po.PoLineId(w.Small);

        var dates = await H.PostWoAsync(w.WoRequest(po, [(line, 5)], start: Today(10), target: Today(5)));
        Assert.Contains("targetDate", ProblemAssert.Is(dates, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var notes = await H.PostWoAsync(w.WoRequest(po, [(line, 5)], notes: new string('x', 1001)));
        Assert.Contains("notes", ProblemAssert.Is(notes, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var negative = await H.PostWoAsync(w.WoRequest(po, [(line, -1), (line, 5)]));
        var errors = ProblemAssert.Is(negative, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!;
        Assert.Contains("lines[0].qty", errors.Keys);
        Assert.Contains("lines[1].poLineId", errors.Keys);

        var foreign = await H.PostWoAsync(w.WoRequest(po, [(otherPo.PoLineId(w.Small), 5)]));
        Assert.Contains("lines[0].poLineId", ProblemAssert.Is(foreign, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var unknownPo = await H.PostWoAsync(w.WoRequest(po, [(line, 5)]) with { PoId = Guid.NewGuid() });
        ProblemAssert.Is(unknownPo, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
    }

    [Fact]
    public async Task Qty_melebihi_Sisa_Rencana_ditolak_dengan_batas_terbaru_dan_alokasi_PO_ikut_dihitung()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var line = po.PoLineId(w.Small);
        (await H.Api.PostAsync($"/api/v1/po-lines/{line}/allocate", new AllocateRequest(30, null), H.AdminToken)).Ok();
        await H.NewWoAsync(w, po, (line, 50));

        var response = await H.PostWoAsync(w.WoRequest(po, [(line, 21)]));

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.PoRemainingPlanExceeded, problem.Code);
        Assert.Equal((21m, 20m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));
        Assert.Equal(1, (await H.PostWoAsync(w.WoRequest(po, [(line, 20)]))).IsSuccess ? 1 : 0);
        await H.AssertInProductionInvariantAsync(po.Id);
    }

    [Fact]
    public async Task WO_hanya_dari_PO_Confirmed_atau_InProgress()
    {
        var w = await H.NewPoWorldAsync();
        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        var cancelled = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        (await H.Api.PostAsync($"/api/v1/purchase-orders/{cancelled.Id}/cancel", new Gms.Shared.Common.ReasonRequest("batal", cancelled.RowVersion), H.AdminToken)).Ok();

        ProblemAssert.Is(await H.PostWoAsync(w.WoRequest(draft, [(draft.PoLineId(w.Small), 5)])), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.PostWoAsync(w.WoRequest(cancelled, [(cancelled.PoLineId(w.Small), 5)])), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/work-orders/creation-options?poId={draft.Id}", H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Opsi_pembuatan_menampilkan_Sisa_Rencana_per_sel_dan_kesiapan_FG()
    {
        var w = await H.NewPoWorldAsync();
        var notReady = await H.NewReadyFgAsync(w.Customer.Id, fillTariffs: false);
        var po = await H.NewPoAsync(w, [
            w.Line(w.Small, 100), w.Line(w.Medium, 80),
            new Gms.Shared.CustomerPo.SavePoLine(null, notReady.Fg.Id, w.Color.Id, notReady.Small.Id, Today(30), 10, null)]);
        await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 40));

        var options = (await H.Api.GetAsync($"/api/v1/work-orders/creation-options?poId={po.Id}", H.AdminToken)).Ok().As<WoCreationOptionsDto>();

        Assert.Equal(po.Code, options.PoCode);
        Assert.Equal(2, options.Fgs.Count);
        var ready = options.Fgs.Single(f => f.FgId == w.Fg.Fg.Id);
        Assert.True(ready.IsReady);
        Assert.Equal(2, ready.Cells.Count);
        Assert.Equal(60, ready.Cells.Single(c => c.ColorSize.SizeId == w.Small.Id).RemainingPlanQty);
        Assert.Equal(80, ready.Cells.Single(c => c.ColorSize.SizeId == w.Medium.Id).RemainingPlanQty);
        var other = options.Fgs.Single(f => f.FgId == notReady.Fg.Id);
        Assert.False(other.IsReady);
        Assert.Contains(other.Readiness, r => !r.Ok);
    }

    [Fact]
    public async Task Draft_dapat_diubah_qty_sel_tanggal_dan_keterangan_serta_Dalam_Produksi_mengikuti()
    {
        var (w, po, wo) = await H.NewWoSetupAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);

        // Naik ke batas penuh pada S, M dihapus (qty 0) dan S tetap; naikkan S ke 100 = Sisa Rencana + qty sendiri.
        var request = w.WoRequest(po, [(po.PoLineId(w.Small), 100), (po.PoLineId(w.Medium), 0)],
            start: Today(1), target: Today(20), notes: "revisi", rowVersion: wo.RowVersion);
        var updated = (await H.PutWoAsync(wo.Id, request)).Ok().As<WoDto>();

        Assert.Single(updated.Lines);
        Assert.Equal((100, Today(1), Today(20), "revisi"), (updated.TotalQty, updated.StartDate!.Value, updated.TargetDate!.Value, updated.Notes));
        var afterPo = await H.GetPoAsync(po.Id);
        Assert.Equal(100, afterPo.Lines.Single(l => l.Id == po.PoLineId(w.Small)).InProductionQty);
        Assert.Equal(100, afterPo.Lines.Single(l => l.Id == po.PoLineId(w.Medium)).RemainingPlanQty);

        // Sel yang dihapus dapat ditambahkan lagi.
        var readded = (await H.PutWoAsync(wo.Id, w.WoRequest(po, [(po.PoLineId(w.Small), 100), (po.PoLineId(w.Medium), 25)], rowVersion: updated.RowVersion))).Ok().As<WoDto>();
        Assert.Equal(2, readded.Lines.Count);
        await H.AssertInProductionInvariantAsync(po.Id);

        var beyond = await H.PutWoAsync(wo.Id, w.WoRequest(po, [(po.PoLineId(w.Small), 101)], rowVersion: readded.RowVersion));
        Assert.Equal(ProblemCodes.PoRemainingPlanExceeded, ProblemAssert.Is(beyond, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
    }

    [Fact]
    public async Task PO_dan_FG_WO_Draft_tidak_dapat_diganti_dan_versi_usang_ditolak()
    {
        var (w, po, wo) = await H.NewWoSetupAsync();
        var otherPo = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);

        var changePo = await H.PutWoAsync(wo.Id, w.WoRequest(otherPo, [(otherPo.PoLineId(w.Small), 5)], rowVersion: wo.RowVersion));
        Assert.Contains("poId", ProblemAssert.Is(changePo, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var changeFg = await H.PutWoAsync(wo.Id, w.WoRequest(po, [(po.PoLineId(w.Small), 5)], rowVersion: wo.RowVersion) with { FgId = Guid.NewGuid() });
        Assert.Contains("fgId", ProblemAssert.Is(changeFg, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        (await H.PutWoAsync(wo.Id, w.WoRequest(po, [(po.PoLineId(w.Small), 5)], notes: "A", rowVersion: wo.RowVersion))).Ok();
        var stale = await H.PutWoAsync(wo.Id, w.WoRequest(po, [(po.PoLineId(w.Small), 6)], notes: "B", rowVersion: wo.RowVersion));
        ProblemAssert.Is(stale, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);

        ProblemAssert.Is(await H.PutWoAsync(Guid.NewGuid(), w.WoRequest(po, [(po.PoLineId(w.Small), 5)])), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Pembatalan_Draft_mengembalikan_Sisa_Rencana_dan_WO_batal_tidak_dapat_diubah()
    {
        var (w, po, wo) = await H.NewWoSetupAsync(poQty: 100, woQtySmall: 70, woQtyMedium: 70);

        var cancelled = (await H.WoActionAsync(wo.Id, "cancel", wo, "salah buat")).Ok().As<WoDto>();

        Assert.Equal((WoStatus.Cancelled, "salah buat"), (cancelled.Status, cancelled.CancelReason));
        Assert.Empty(cancelled.AllowedActions);
        var afterPo = await H.GetPoAsync(po.Id);
        Assert.All(afterPo.Lines, l => Assert.Equal((0, 100), (l.InProductionQty, l.RemainingPlanQty)));
        await H.AssertInProductionInvariantAsync(po.Id);

        ProblemAssert.Is(await H.PutWoAsync(wo.Id, w.WoRequest(po, [(po.PoLineId(w.Small), 5)], rowVersion: cancelled.RowVersion)), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "release", cancelled), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "cancel", cancelled, "lagi"), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Pembatalan_wajib_beralasan()
    {
        var (_, _, wo) = await H.NewWoSetupAsync();

        var noReason = await H.WoActionAsync(wo.Id, "cancel", wo, "  ");

        Assert.Contains("reason", ProblemAssert.Is(noReason, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Equal(WoStatus.Draft, (await H.GetWoAsync(wo.Id)).Status);
    }

    [Fact]
    public async Task Permission_dipisah_untuk_lihat_buat_dan_ubah()
    {
        var (w, po, wo) = await H.NewWoSetupAsync();
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.WorkOrder.View)).Id);
        var creator = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.WorkOrder.View, Permissions.WorkOrder.Create)).Id);
        var none = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);

        Assert.True((await H.Api.GetAsync("/api/v1/work-orders", viewer.Token)).IsSuccess);
        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/work-orders", none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.PostWoAsync(w.WoRequest(po, [(po.PoLineId(w.Small), 1)]), viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.PostWoAsync(w.WoRequest(po, [(po.PoLineId(w.Small), 1)]), creator.Token)).IsSuccess);
        ProblemAssert.Is(await H.PutWoAsync(wo.Id, w.WoRequest(po, [(po.PoLineId(w.Small), 1)], rowVersion: wo.RowVersion), creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "cancel", wo, "x", creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);

        var shown = (await H.Api.GetAsync($"/api/v1/work-orders/{wo.Id}", viewer.Token)).Ok().As<WoDto>();
        Assert.Empty(shown.AllowedActions);
    }

    // ===== KONKURENSI =====

    [Fact]
    public async Task Dua_pembuat_WO_bersamaan_untuk_sisa_yang_sama_tepat_satu_berhasil()
    {
        for (var round = 0; round < 5; round++)
        {
            var w = await H.NewPoWorldAsync();
            var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
            var line = po.PoLineId(w.Small);
            var gate = new TaskCompletionSource();

            Task<ApiResponse> Go() => Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostWoAsync(w.WoRequest(po, [(line, 70)]));
            });

            var tasks = new[] { Go(), Go() };
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            var loser = results.Single(r => !r.IsSuccess);
            Assert.Equal(ProblemCodes.PoRemainingPlanExceeded, ProblemAssert.Is(loser, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
            Assert.Equal(30m, ProblemAssert.Is(loser, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Violations![0].Limit);
            await H.AssertInProductionInvariantAsync(po.Id);
        }
    }

    [Fact]
    public async Task Dua_puluh_pembuat_WO_paralel_tidak_pernah_melampaui_Sisa_Rencana()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var line = po.PoLineId(w.Small);
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await H.PostWoAsync(w.WoRequest(po, [(line, 10)]));
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, results.Count(r => r.IsSuccess));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        var afterPo = await H.GetPoAsync(po.Id);
        Assert.Equal((100, 0), (afterPo.Lines[0].InProductionQty, afterPo.Lines[0].RemainingPlanQty));
        await H.AssertInProductionInvariantAsync(po.Id);
    }

    [Fact]
    public async Task Pembuatan_WO_bersamaan_dengan_pembatalan_PO_tidak_meninggalkan_WO_aktif_pada_PO_batal()
    {
        for (var round = 0; round < 4; round++)
        {
            var w = await H.NewPoWorldAsync();
            var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
            var gate = new TaskCompletionSource();

            var create = Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostWoAsync(w.WoRequest(po, [(po.PoLineId(w.Small), 10)]));
            });
            var cancel = Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel",
                    new Gms.Shared.Common.ReasonRequest("batal bersamaan", po.RowVersion), H.AdminToken);
            });
            gate.SetResult();
            var results = await Task.WhenAll(create, cancel);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            var afterPo = await H.GetPoAsync(po.Id);
            await using var db = H.CreateDbContext();
            var activeWos = await db.WorkOrders.CountAsync(x => x.PoId == po.Id && x.Status != WoStatus.Cancelled && x.Status != WoStatus.Closed);
            Assert.False(afterPo.Status == PoStatus.Cancelled && activeWos > 0, "PO batal tidak boleh memiliki WO aktif");
            await H.AssertInProductionInvariantAsync(po.Id);
        }
    }
}
