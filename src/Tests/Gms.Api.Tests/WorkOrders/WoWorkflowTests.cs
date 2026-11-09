using System.Net;
using System.Text.Json;
using Gms.Api.Tests.Infrastructure;
using Gms.Domain.Production;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.WorkOrders;

/// <summary>Rilis + snapshot, perubahan Qty, siklus Completed/Closed/Reopen, pembatalan, dan pembacaan progres (FSD Work Order 4.3–4.8).</summary>
[Collection(ApiCollection.Name)]
public sealed class WoWorkflowTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    // ===== Rilis dan snapshot =====

    [Fact]
    public async Task Rilis_membuat_snapshot_routing_dan_tarif_serta_mencatat_pelaku_riwayat_dan_audit()
    {
        var (w, _, draft) = await H.NewWoSetupAsync();

        var released = await H.WoDoAsync(draft.Id, "release");

        Assert.Equal(WoStatus.Released, released.Status);
        Assert.NotNull(released.ReleasedAt);
        Assert.Contains(WoActions.EditQuantities, released.AllowedActions);
        Assert.DoesNotContain(WoActions.Release, released.AllowedActions);

        var snapshot = (await H.Api.GetAsync($"/api/v1/work-orders/{draft.Id}/tariff-snapshot", H.AdminToken)).Ok().As<WoTariffSnapshotDto>();
        Assert.Equal([ProcessType.Internal, ProcessType.Vendor, ProcessType.Internal], snapshot.Steps.Select(s => s.ProcessType));
        Assert.Equal([w.Fg.Cut.Code, w.Fg.Emb.Code, w.Fg.Sew.Code], snapshot.Steps.Select(s => s.ProcessCode));
        Assert.Equal(2, snapshot.Sizes.Count);
        Assert.Equal(4, snapshot.Rates.Count); // 2 langkah Internal × 2 size; langkah Vendor tidak bertarif
        Assert.DoesNotContain(snapshot.Rates, r => r.Seq == snapshot.Steps[1].Seq);
        Assert.False(string.IsNullOrEmpty(snapshot.ReleasedByName));

        var grid = await H.GetGridAsync(w.Fg.Fg.Id);
        foreach (var rate in snapshot.Rates)
        {
            var process = snapshot.Steps.Single(s => s.Seq == rate.Seq).ProcessCode == w.Fg.Cut.Code ? w.Fg.Cut.Id : w.Fg.Sew.Id;
            Assert.Equal(grid.Cells.Single(c => c.ProcessId == process && c.SizeId == rate.SizeId).Rate, rate.Rate);
        }

        var history = (await H.Api.GetAsync($"/api/v1/work-orders/{draft.Id}/status-history", H.AdminToken)).Ok().As<List<WoStatusHistoryItem>>();
        Assert.Equal([WoStatus.Draft, WoStatus.Released], history.Select(x => x.ToStatus));
        Assert.Equal(WoStatus.Draft, history[1].FromStatus);

        await using var db = H.CreateDbContext();
        Assert.Contains("WoReleased", await db.AuditLogs.Where(a => a.TargetId == draft.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Perubahan_tarif_master_setelah_rilis_tidak_memengaruhi_snapshot()
    {
        var (w, _, draft) = await H.NewWoSetupAsync();
        await H.WoDoAsync(draft.Id, "release");
        var before = (await H.Api.GetAsync($"/api/v1/work-orders/{draft.Id}/tariff-snapshot", H.AdminToken)).Ok().As<WoTariffSnapshotDto>();

        await H.FillAllTariffsAsync(w.Fg.Fg.Id, baseRate: 9000m);

        var after = (await H.Api.GetAsync($"/api/v1/work-orders/{draft.Id}/tariff-snapshot", H.AdminToken)).Ok().As<WoTariffSnapshotDto>();
        Assert.Equal(before.Rates.OrderBy(r => r.Seq).ThenBy(r => r.SizeId), after.Rates.OrderBy(r => r.Seq).ThenBy(r => r.SizeId));
        Assert.All(after.Rates, r => Assert.True(r.Rate < 9000m));
    }

    [Fact]
    public async Task Tarif_snapshot_bergantung_pada_permission_khusus_dan_tidak_bocor_lewat_detail_atau_progres()
    {
        var (_, _, wo) = await H.NewReleasedWoAsync();
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.WorkOrder.View)).Id);
        var payroll = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.WorkOrder.View, Permissions.WorkOrder.Tarif.View)).Id);

        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/work-orders/{wo.Id}/tariff-snapshot", viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.Api.GetAsync($"/api/v1/work-orders/{wo.Id}/tariff-snapshot", payroll.Token)).IsSuccess);

        foreach (var path in new[] { $"/api/v1/work-orders/{wo.Id}", $"/api/v1/work-orders/{wo.Id}/progress", "/api/v1/work-orders?pageSize=5" })
        {
            var body = (await H.Api.GetAsync(path, viewer.Token)).Ok().Body;
            Assert.DoesNotContain("rate", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tarif", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Rilis_ditolak_untuk_FG_belum_siap_dengan_alasan_spesifik_dan_Draft_tetap_dapat_diubah()
    {
        var w = await H.NewPoWorldAsync();
        var notReady = await H.NewReadyFgAsync(w.Customer.Id, fillTariffs: false);
        var po = await H.NewPoAsync(w, [new SavePoLine(null, notReady.Fg.Id, w.Color.Id, notReady.Small.Id, Today(30), 50, null)]);
        var request = new SaveWoRequest(po.Id, notReady.Fg.Id, null, null, null, [new SaveWoLine(po.Lines[0].Id, 20)]);
        var draft = (await H.PostWoAsync(request)).Ok().As<WoDto>();

        var preview = (await H.Api.PostAsync($"/api/v1/work-orders/{draft.Id}/release/preview", new { }, H.AdminToken)).Ok().As<WoReleasePreviewDto>();
        Assert.False(preview.CanRelease);
        Assert.Contains(preview.Checks, c => !c.Ok && c.Code.StartsWith("fg.", StringComparison.Ordinal));

        var blocked = await H.WoActionAsync(draft.Id, "release", draft);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.FgNotReady, problem.Code);
        Assert.NotEmpty(problem.Violations!);
        Assert.Equal(WoStatus.Draft, (await H.GetWoAsync(draft.Id)).Status);

        await H.FillAllTariffsAsync(notReady.Fg.Id);
        var now = await H.GetWoAsync(draft.Id);
        (await H.Api.PutAsync($"/api/v1/work-orders/{draft.Id}/schedule", new UpdateWoScheduleRequest(null, Today(9), "siap", now.RowVersion), H.AdminToken)).Ok();
        Assert.Equal(WoStatus.Released, (await H.WoDoAsync(draft.Id, "release")).Status);
    }

    [Fact]
    public async Task Pratinjau_rilis_menghitung_langkah_dan_sel_tarif_untuk_WO_siap()
    {
        var (_, _, wo) = await H.NewWoSetupAsync();

        var preview = (await H.Api.PostAsync($"/api/v1/work-orders/{wo.Id}/release/preview", new { }, H.AdminToken)).Ok().As<WoReleasePreviewDto>();

        Assert.True(preview.CanRelease);
        Assert.All(preview.Checks, c => Assert.True(c.Ok, c.Message));
        Assert.Equal((3, 4), (preview.RoutingStepCount, preview.TariffCellCount));
    }

    [Fact]
    public async Task Rilis_ditolak_bila_PO_sudah_Completed_atau_dibatalkan_namun_Draft_dapat_dibatalkan()
    {
        var (_, po, wo) = await H.NewWoSetupAsync();
        await using (var db = H.CreateDbContext())
        {
            await db.PurchaseOrders.Where(p => p.Id == po.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PoStatus.Completed));
        }

        var blocked = await H.WoActionAsync(wo.Id, "release", wo);

        ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        var cancelled = (await H.WoActionAsync(wo.Id, "cancel", wo, "PO selesai")).Ok().As<WoDto>();
        Assert.Equal(WoStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Dua_penekan_Rilis_bersamaan_tepat_satu_berhasil_dan_snapshot_tidak_ganda()
    {
        for (var round = 0; round < 4; round++)
        {
            var (_, _, wo) = await H.NewWoSetupAsync();
            var gate = new TaskCompletionSource();

            Task<ApiResponse> Go() => Task.Run(async () =>
            {
                await gate.Task;
                return await H.WoActionAsync(wo.Id, "release", wo);
            });

            var tasks = new[] { Go(), Go() };
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            // Yang kalah menunggu kunci lalu melihat status sudah berubah: 409 (state-conflict) atau 409 (concurrency-conflict), keduanya sah (E1).
            var loser = results.Single(r => !r.IsSuccess);
            Assert.Equal(HttpStatusCode.Conflict, loser.Status);
            Assert.Contains(loser.Problem.Type, new[] { ProblemTypes.StateConflict, ProblemTypes.ConcurrencyConflict });
            await using var db = H.CreateDbContext();
            Assert.Equal(3, await db.WoRoutingSnapshots.CountAsync(s => s.WoId == wo.Id));
            Assert.Equal(1, await db.WoStatusHistory.CountAsync(s => s.WoId == wo.Id && s.ToStatus == WoStatus.Released));
        }
    }

    // ===== Perubahan setelah rilis =====

    [Fact]
    public async Task Qty_WO_Released_dapat_dinaikkan_dalam_Sisa_Rencana_dan_diturunkan_dengan_audit_sebelum_sesudah()
    {
        var (w, po, wo) = await H.NewReleasedWoAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);
        var s = po.PoLineId(w.Small);
        var m = po.PoLineId(w.Medium);

        var up = (await H.PutQuantitiesAsync(wo, [(s, 100), (m, 40)])).Ok().As<WoDto>();
        Assert.Equal(140, up.TotalQty);
        Assert.Equal(0, (await H.GetPoAsync(po.Id)).Lines.Single(l => l.Id == s).RemainingPlanQty);

        var down = (await H.PutQuantitiesAsync(up, [(s, 20), (m, 40)])).Ok().As<WoDto>();
        Assert.Equal(60, down.TotalQty);
        Assert.Equal(80, (await H.GetPoAsync(po.Id)).Lines.Single(l => l.Id == s).RemainingPlanQty);
        await H.AssertInProductionInvariantAsync(po.Id);

        await using var db = H.CreateDbContext();
        var audit = await db.AuditLogs.Where(a => a.TargetId == wo.Id.ToString() && a.EventType == "WoQuantitiesChanged").ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.Contains(audit, a => (a.OldValue ?? "").Contains("60") && (a.NewValue ?? "").Contains("100"));
    }

    [Fact]
    public async Task Qty_tidak_boleh_di_bawah_output_proses_pertama_tetapi_boleh_sama()
    {
        var (w, po, wo) = await H.NewReleasedWoAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);
        var s = po.PoLineId(w.Small);
        var m = po.PoLineId(w.Medium);
        var smallLine = wo.Lines.Single(l => l.PoLineId == s);
        await H.SetStepBalanceAsync(smallLine.Id, seq: 1, qty: 35);
        await H.SetWoStatusAsync(wo.Id, WoStatus.InProgress);
        var current = await H.GetWoAsync(wo.Id);

        var below = await H.PutQuantitiesAsync(current, [(s, 34), (m, 40)]);
        var problem = ProblemAssert.Is(below, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.WoQtyBelowOutput, problem.Code);
        Assert.Equal((34m, 35m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));

        var equal = (await H.PutQuantitiesAsync(current, [(s, 35), (m, 40)])).Ok().As<WoDto>();
        Assert.Equal(35, equal.Lines.Single(l => l.PoLineId == s).Qty);
    }

    [Fact]
    public async Task Baris_tidak_dapat_ditambah_atau_dihapus_dan_kenaikan_melebihi_Sisa_Rencana_ditolak()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 100)]);
        var s = po.PoLineId(w.Small);
        var m = po.PoLineId(w.Medium);
        var wo = await H.WoDoAsync((await H.NewWoAsync(w, po, (s, 60))).Id, "release");
        await H.NewWoAsync(w, po, (s, 30)); // WO lain menyisakan 10 pada sel S

        var add = await H.PutQuantitiesAsync(wo, [(s, 60), (m, 10)]);
        Assert.Contains("lines[1].poLineId", ProblemAssert.Is(add, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var remove = await H.PutQuantitiesAsync(wo, []);
        Assert.Contains("lines", ProblemAssert.Is(remove, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var zero = await H.PutQuantitiesAsync(wo, [(s, 0)]);
        Assert.Contains("lines[0].qty", ProblemAssert.Is(zero, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var tooMuch = await H.PutQuantitiesAsync(wo, [(s, 71)]);
        var problem = ProblemAssert.Is(tooMuch, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal((71m, 70m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));
        Assert.Equal(70, (await H.PutQuantitiesAsync(wo, [(s, 70)])).Ok().As<WoDto>().TotalQty);
    }

    [Fact]
    public async Task Perubahan_Qty_hanya_pada_Released_atau_InProgress()
    {
        var (w, po, draft) = await H.NewWoSetupAsync();
        var cells = new[] { (po.PoLineId(w.Small), 10), (po.PoLineId(w.Medium), 10) };

        ProblemAssert.Is(await H.PutQuantitiesAsync(draft, cells), HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        var released = await H.WoDoAsync(draft.Id, "release");
        await H.SetWoStatusAsync(draft.Id, WoStatus.Completed);
        ProblemAssert.Is(await H.PutQuantitiesAsync(await H.GetWoAsync(draft.Id), cells), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        Assert.Equal(WoStatus.Released, released.Status);
    }

    [Fact]
    public async Task Jadwal_dapat_diubah_sampai_InProgress_tetapi_target_tidak_boleh_sebelum_mulai()
    {
        var (_, _, wo) = await H.NewReleasedWoAsync();
        var path = $"/api/v1/work-orders/{wo.Id}/schedule";

        var bad = await H.Api.PutAsync(path, new UpdateWoScheduleRequest(Today(10), Today(1), null, wo.RowVersion), H.AdminToken);
        Assert.Contains("targetDate", ProblemAssert.Is(bad, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var ok = (await H.Api.PutAsync(path, new UpdateWoScheduleRequest(Today(1), Today(10), "jadwal baru", wo.RowVersion), H.AdminToken)).Ok().As<WoDto>();
        Assert.Equal((Today(1), Today(10), "jadwal baru"), (ok.StartDate!.Value, ok.TargetDate!.Value, ok.Notes));

        await H.SetWoStatusAsync(wo.Id, WoStatus.Closed);
        var closed = await H.GetWoAsync(wo.Id);
        ProblemAssert.Is(await H.Api.PutAsync(path, new UpdateWoScheduleRequest(null, null, null, closed.RowVersion), H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Completed / Closed / Reopen =====

    [Fact]
    public async Task Complete_hanya_dari_InProgress_resume_wajib_beralasan_dan_riwayat_tercatat()
    {
        var (_, _, wo) = await H.NewReleasedWoAsync();

        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "complete", wo), HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        await H.SetWoStatusAsync(wo.Id, WoStatus.InProgress);
        var completed = await H.WoDoAsync(wo.Id, "complete");
        Assert.Equal(WoStatus.Completed, completed.Status);
        Assert.Contains(WoActions.Close, completed.AllowedActions);
        Assert.Contains(WoActions.Resume, completed.AllowedActions);

        var noReason = await H.WoActionAsync(wo.Id, "resume", completed, " ");
        Assert.Contains("reason", ProblemAssert.Is(noReason, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var resumed = (await H.WoActionAsync(wo.Id, "resume", completed, "ada output susulan")).Ok().As<WoDto>();
        Assert.Equal(WoStatus.InProgress, resumed.Status);

        var history = (await H.Api.GetAsync($"/api/v1/work-orders/{wo.Id}/status-history", H.AdminToken)).Ok().As<List<WoStatusHistoryItem>>();
        Assert.Equal(WoStatus.InProgress, history[^1].ToStatus);
        Assert.Equal("ada output susulan", history[^1].Reason);
        Assert.All(history.Skip(1), x => Assert.False(string.IsNullOrEmpty(x.ChangedByName)));
    }

    [Fact]
    public async Task Close_mengembalikan_sisa_ke_Sisa_Rencana_PO_dan_preview_menyebut_output_belum_diserahterimakan()
    {
        var (w, po, wo) = await H.NewReleasedWoAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);
        var smallLine = wo.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id);
        await H.SetStepBalanceAsync(smallLine.Id, seq: 3, qty: 50);
        await H.SetHandedOverAsync(smallLine.Id, 20);
        await H.SetWoStatusAsync(wo.Id, WoStatus.Completed);
        await using (var db = H.CreateDbContext())
        {
            // Serah terima 20 pcs sudah terhitung pada Dalam Produksi (qty − diserahterimakan).
            await Gms.Api.Features.Sales.PoLifecycle.RecomputeInProductionAsync(db, [po.PoLineId(w.Small), po.PoLineId(w.Medium)], default);
        }

        var preview = (await H.Api.PostAsync($"/api/v1/work-orders/{wo.Id}/close/preview", new { }, H.AdminToken)).Ok().As<WoClosePreviewDto>();
        Assert.Equal(40 + 40, preview.RemainingInProductionQty); // (60−20) + 40
        Assert.Equal(preview.RemainingInProductionQty, preview.ReturnedToPoPlanQty);
        var cell = Assert.Single(preview.UnhandedCells);
        Assert.Equal((w.Small.Id, 30), (cell.ColorSize.SizeId, cell.Qty)); // 50 output − 20 diserahterimakan

        var closed = await H.WoDoAsync(wo.Id, "close");

        Assert.Equal(WoStatus.Closed, closed.Status);
        Assert.Equal([WoActions.Reopen], closed.AllowedActions);
        var afterPo = await H.GetPoAsync(po.Id);
        Assert.All(afterPo.Lines, l => Assert.Equal(0, l.InProductionQty));
        Assert.Equal(100, afterPo.Lines.Single(l => l.Id == po.PoLineId(w.Medium)).RemainingPlanQty);
        await H.AssertInProductionInvariantAsync(po.Id);
    }

    [Fact]
    public async Task Close_hanya_dari_Completed_dan_preview_menolak_status_lain()
    {
        var (_, _, wo) = await H.NewReleasedWoAsync();

        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "close", wo), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.Api.PostAsync($"/api/v1/work-orders/{wo.Id}/close/preview", new { }, H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Reopen_wajib_beralasan_berhasil_bila_Sisa_Rencana_cukup_dan_ditolak_dengan_selisih_bila_tidak()
    {
        var (w, po, wo) = await H.NewReleasedWoAsync(poQty: 100, woQtySmall: 80, woQtyMedium: 10);
        await H.SetWoStatusAsync(wo.Id, WoStatus.Completed);
        var closed = await H.WoDoAsync(wo.Id, "close");
        var s = po.PoLineId(w.Small);

        var noReason = await H.WoActionAsync(wo.Id, "reopen", closed, null);
        Assert.Contains("reason", ProblemAssert.Is(noReason, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        await H.NewWoAsync(w, po, (s, 50)); // WO lain memakai Sisa Rencana: tinggal 50, butuh 80
        var rejected = await H.WoActionAsync(wo.Id, "reopen", closed, "koreksi");
        var problem = ProblemAssert.Is(rejected, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.PoRemainingPlanExceeded, problem.Code);
        Assert.Equal((80m, 50m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));
        Assert.Contains("30", problem.Violations[0].Message);
        Assert.Equal(WoStatus.Closed, (await H.GetWoAsync(wo.Id)).Status);

        var other = await H.GetWoAsync((await H.Api.GetAsync($"/api/v1/work-orders?poId={po.Id}&pageSize=50", H.AdminToken)).Ok().As<PagedResult<WoListItem>>()
            .Items.Single(i => i.Id != wo.Id).Id);
        await H.WoActionAsync(other.Id, "cancel", other, "beri ruang");

        var reopened = (await H.WoActionAsync(wo.Id, "reopen", await H.GetWoAsync(wo.Id), "koreksi")).Ok().As<WoDto>();
        Assert.Equal(WoStatus.Completed, reopened.Status);
        Assert.Equal(80, (await H.GetPoAsync(po.Id)).Lines.Single(l => l.Id == s).InProductionQty);
        await H.AssertInProductionInvariantAsync(po.Id);
    }

    [Fact]
    public async Task Reopen_ditolak_bila_PO_sudah_dibatalkan()
    {
        var (_, po, wo) = await H.NewReleasedWoAsync();
        await H.SetWoStatusAsync(wo.Id, WoStatus.Completed);
        var closed = await H.WoDoAsync(wo.Id, "close");
        (await H.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel",
            new ReasonRequest("batal", (await H.GetPoAsync(po.Id)).RowVersion), H.AdminToken)).Ok();

        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "reopen", closed, "koreksi"), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Pembatalan WO Released =====

    [Fact]
    public async Task Cancel_Released_tanpa_aktivitas_berhasil_dan_dengan_serah_terima_ditolak()
    {
        var (w, po, wo) = await H.NewReleasedWoAsync();
        var cancelledOk = (await H.WoActionAsync(wo.Id, "cancel", wo, "tidak jadi")).Ok().As<WoDto>();
        Assert.Equal(WoStatus.Cancelled, cancelledOk.Status);
        await H.AssertInProductionInvariantAsync(po.Id);

        var second = await H.WoDoAsync((await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 10))).Id, "release");
        await H.SetHandedOverAsync(second.Lines[0].Id, 1);
        var blocked = await H.WoActionAsync(second.Id, "cancel", await H.GetWoAsync(second.Id), "tidak jadi");
        Assert.Equal(ProblemCodes.WoHasActivity, ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        Assert.Equal(WoStatus.Released, (await H.GetWoAsync(second.Id)).Status);
    }

    [Fact]
    public async Task Cancel_ditolak_bila_ada_output_atau_WO_sudah_InProgress()
    {
        var (_, _, wo) = await H.NewReleasedWoAsync();
        await using (var db = H.CreateDbContext())
        {
            var worker = await H.NewWorkerAsync();
            var entry = new OutputEntry { WoId = wo.Id, Seq = 1, WorkerId = worker.Id, WorkDate = Today(), CreatedAt = DateTimeOffset.UtcNow };
            entry.Lines.Add(new OutputEntryLine { WoLineId = wo.Lines[0].Id, Qty = 5 });
            db.OutputEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var withOutput = await H.WoActionAsync(wo.Id, "cancel", await H.GetWoAsync(wo.Id), "x");
        Assert.Equal(ProblemCodes.WoHasActivity, ProblemAssert.Is(withOutput, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        await H.SetWoStatusAsync(wo.Id, WoStatus.InProgress);
        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "cancel", await H.GetWoAsync(wo.Id), "x"), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Aksi_status_dilindungi_permission_masing_masing()
    {
        var (_, _, wo) = await H.NewWoSetupAsync();
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.WorkOrder.View)).Id);
        var konveksi = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.WorkOrder.View, Permissions.WorkOrder.Complete)).Id);

        foreach (var action in new[] { "release", "cancel", "close", "reopen" })
        {
            ProblemAssert.Is(await H.WoActionAsync(wo.Id, action, wo, "x", konveksi.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        }

        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "complete", wo, null, viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.WoActionAsync(wo.Id, "complete", wo, null, konveksi.Token), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Progres dan daftar =====

    [Fact]
    public async Task Matriks_progres_menampilkan_kumulatif_per_langkah_status_vendor_dan_serah_terima()
    {
        var (w, _, wo) = await H.NewReleasedWoAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);
        var s = wo.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Id;
        var m = wo.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id).Id;
        await H.SetStepBalanceAsync(s, 1, 60);
        await H.SetStepBalanceAsync(m, 1, 40);
        await H.SetStepBalanceAsync(s, 2, 30, sent: 50);   // vendor: diterima 30, terkirim 50
        await H.SetStepBalanceAsync(s, 3, 25);
        await H.SetHandedOverAsync(s, 10);
        await H.SetWoStatusAsync(wo.Id, WoStatus.InProgress);

        var progress = (await H.Api.GetAsync($"/api/v1/work-orders/{wo.Id}/progress", H.AdminToken)).Ok().As<WoProgressDto>();

        Assert.Equal(3, progress.Steps.Count);
        Assert.Equal(2, progress.Columns.Count);
        var cut = progress.Steps[0];
        Assert.Equal((100, 100, 100m, StepState.Done), (cut.TotalOutput, cut.TotalTarget, cut.Percent, cut.State));
        var vendor = progress.Steps[1];
        Assert.Equal((ProcessType.Vendor, 30, 50, 20, StepState.InProgress), (vendor.ProcessType, vendor.TotalOutput, vendor.SentTotal, vendor.OutstandingAtVendor, vendor.State));
        var sew = progress.Steps[2];
        Assert.Equal((25, 25m, StepState.InProgress), (sew.TotalOutput, sew.Percent, sew.State));
        Assert.Equal((10, 15), (progress.HandedOverTotal, progress.UnhandedTotal)); // 25 output akhir − 10 diserahterimakan

        var detail = await H.GetWoAsync(wo.Id);
        Assert.Equal((25m, 10, 15, 20), (detail.ProgressPercent, detail.HandedOverQty, detail.UnhandedQty, detail.OutstandingAtVendorQty));
    }

    [Fact]
    public async Task Progres_di_atas_100_persen_ditampilkan_apa_adanya_dan_WO_Draft_tanpa_langkah()
    {
        var (w, _, draft) = await H.NewWoSetupAsync(woQtySmall: 50, woQtyMedium: 10);
        Assert.Empty((await H.Api.GetAsync($"/api/v1/work-orders/{draft.Id}/progress", H.AdminToken)).Ok().As<WoProgressDto>().Steps);

        var released = await H.WoDoAsync(draft.Id, "release");
        await H.SetStepBalanceAsync(released.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Id, 3, 75);
        await H.SetStepBalanceAsync(released.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id).Id, 3, 15);

        Assert.Equal(150m, (await H.GetWoAsync(draft.Id)).ProgressPercent);
    }

    [Fact]
    public async Task Daftar_menyaring_status_PO_terlambat_menunggu_close_dan_mencari_kode()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var line = po.PoLineId(w.Small);
        var late = await H.NewWoAsync(w, po, (line, 10));
        await H.Api.PutAsync($"/api/v1/work-orders/{late.Id}/schedule", new UpdateWoScheduleRequest(Today(-10), Today(-1), null, late.RowVersion), H.AdminToken);
        var onTime = await H.NewWoAsync(w, po, (line, 10));
        var completed = await H.WoDoAsync((await H.NewWoAsync(w, po, (line, 10))).Id, "release");
        await H.SetWoStatusAsync(completed.Id, WoStatus.Completed);

        async Task<List<Guid>> Ids(string query) =>
            (await H.Api.GetAsync($"/api/v1/work-orders?customerId={w.Customer.Id}&pageSize=50{query}", H.AdminToken)).Ok()
            .As<PagedResult<WoListItem>>().Items.Select(i => i.Id).ToList();

        Assert.Equal(3, (await Ids("")).Count);
        Assert.Equal([late.Id], await Ids("&terlambat=true"));
        Assert.Equal([completed.Id], await Ids("&menungguClose=true"));
        Assert.Equal([completed.Id], await Ids("&status=Completed"));
        Assert.Equal(3, (await Ids($"&poId={po.Id}")).Count);
        Assert.Equal(3, (await Ids($"&fgId={w.Fg.Fg.Id}")).Count);
        Assert.Equal([onTime.Id], await Ids($"&q={onTime.Code}"));
        Assert.Equal(3, (await Ids($"&q={po.Code}")).Count);

        var item = (await H.Api.GetAsync($"/api/v1/work-orders?q={late.Code}", H.AdminToken)).Ok().As<PagedResult<WoListItem>>().Items.Single();
        Assert.True(item.IsLate);
        Assert.Equal((po.Code, w.Customer.Name, 10), (item.PoCode, item.CustomerName, item.TotalQty));
    }

    [Fact]
    public async Task Daftar_menghitung_progres_dari_proses_terakhir_dan_mendukung_paging_serta_urutan()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var released = await H.WoDoAsync((await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 40))).Id, "release");
        await H.SetStepBalanceAsync(released.Lines[0].Id, 3, 10);
        await H.NewWoAsync(w, po, (po.PoLineId(w.Small), 10));

        var list = (await H.Api.GetAsync($"/api/v1/work-orders?customerId={w.Customer.Id}&sort=code:asc", H.AdminToken)).Ok().As<PagedResult<WoListItem>>();
        Assert.Equal(25m, list.Items[0].ProgressPercent);
        Assert.Equal(0m, list.Items[1].ProgressPercent);
        Assert.True(string.CompareOrdinal(list.Items[0].Code, list.Items[1].Code) < 0);

        var page2 = (await H.Api.GetAsync($"/api/v1/work-orders?customerId={w.Customer.Id}&sort=code:asc&pageSize=1&page=2", H.AdminToken)).Ok().As<PagedResult<WoListItem>>();
        Assert.Equal((2, 1, list.Items[1].Id), (page2.TotalCount, page2.Items.Count, page2.Items[0].Id));
    }

    [Fact]
    public async Task Detail_tidak_ada_404_dan_perubahan_PO_tertahan_oleh_Dalam_Produksi_WO()
    {
        var (w, po, _) = await H.NewWoSetupAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);

        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/work-orders/{Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/work-orders/{Guid.NewGuid()}/progress", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);

        var current = await H.GetPoAsync(po.Id);
        var tooLow = await H.PutPoAsync(po.Id, current.ToRequest([current.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).AsLine(qty: 59), current.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id).AsLine()]));
        Assert.Equal(ProblemCodes.PoQtyBelowCommitted, ProblemAssert.Is(tooLow, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        var cancelPo = await H.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel", new ReasonRequest("batal", current.RowVersion), H.AdminToken);
        Assert.Equal("po.cancel-blocked", ProblemAssert.Is(cancelPo, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        _ = JsonSerializer.Serialize(po); // memastikan DTO dapat diserialisasi
    }
}
