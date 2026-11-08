using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Sales;

/// <summary>Alokasi stok ke baris PO (FSD Customer PO 4.4) termasuk perilaku konkurensi (ADR-004).</summary>
[Collection(ApiCollection.Name)]
public sealed class PoAllocationTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private static string Allocate(Guid lineId) => $"/api/v1/po-lines/{lineId}/allocate";

    private static string Release(Guid lineId) => $"/api/v1/po-lines/{lineId}/release-allocation";

    private async Task<StockAvailabilityDto> AvailabilityAsync(Guid lineId) =>
        (await H.Api.GetAsync($"/api/v1/po-lines/{lineId}/stock-availability", H.AdminToken)).Ok().As<StockAvailabilityDto>();

    // ===== Ketersediaan =====

    [Fact]
    public async Task Ketersediaan_tanpa_stok_nol_dengan_alasan_dan_dibatasi_stok_atau_sisa_rencana()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 30)]);
        var small = po.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id);
        var medium = po.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id);

        var none = await AvailabilityAsync(small.Id);
        Assert.Equal((0, 0, 0, 100, 0), (none.OnHand, none.AllocatedAllPo, none.Available, none.RemainingPlanQty, none.MaxAllocatable));
        Assert.Contains("Tidak ada stok", none.LimitReason);

        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 60);
        var limitedByStock = await AvailabilityAsync(small.Id);
        Assert.Equal(60, limitedByStock.MaxAllocatable);
        Assert.Contains("stok tersedia", limitedByStock.LimitReason);

        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Medium.Id, 500);
        var limitedByPlan = await AvailabilityAsync(medium.Id);
        Assert.Equal(30, limitedByPlan.MaxAllocatable);
        Assert.Contains("Sisa Rencana", limitedByPlan.LimitReason);
    }

    [Fact]
    public async Task Ketersediaan_mengurangi_alokasi_PO_lain_dan_nol_pada_PO_Draft()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 50);
        var first = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var second = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 100)], confirm: false);

        (await H.Api.PostAsync(Allocate(first.Lines[0].Id), new AllocateRequest(35, null), H.AdminToken)).Ok();

        var view = await AvailabilityAsync(second.Lines[0].Id);
        Assert.Equal((50, 35, 15, 15), (view.OnHand, view.AllocatedAllPo, view.Available, view.MaxAllocatable));
        Assert.Equal(0, view.AllocatedToThisLine);
        var onDraft = await AvailabilityAsync(draft.Lines[0].Id);
        Assert.Equal(0, onDraft.MaxAllocatable);
        Assert.Contains("Confirmed atau In Progress", onDraft.LimitReason);
    }

    // ===== Alokasi & pelepasan =====

    [Fact]
    public async Task Alokasi_mengurangi_stok_tersedia_menambah_alokasi_baris_dan_mengubah_PO_menjadi_InProgress()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 80);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        Assert.Equal(PoStatus.Confirmed, po.Status);

        var line = (await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(30, "untuk pengiriman pertama"), H.AdminToken)).Ok().As<PoLineDto>();

        Assert.Equal(30, line.AllocatedQty);
        Assert.Equal(70, line.RemainingPlanQty);
        Assert.Equal(30, line.MinAllowedQty);
        Assert.True(line.IsLocked);
        Assert.Equal((80, 30), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(po.Id)).Status);
        await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);

        var entries = (await H.Api.GetAsync($"/api/v1/purchase-orders/{po.Id}/allocations", H.AdminToken)).Ok().As<List<AllocationEntryDto>>();
        var entry = Assert.Single(entries);
        Assert.Equal((AllocationKind.Allocate, 30, AllocationSource.Manual, "untuk pengiriman pertama"), (entry.Kind, entry.Qty, entry.Source, entry.Reason));
        Assert.False(string.IsNullOrEmpty(entry.CreatedByName));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Qty_alokasi_harus_positif(int qty)
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 10);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);

        var response = await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(qty, null), H.AdminToken);

        Assert.Contains("qty", ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Fact]
    public async Task Alokasi_melebihi_batas_ditolak_dengan_batas_terbaru_dan_tidak_mengubah_apa_pun()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 20);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);

        var response = await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(21, null), H.AdminToken);

        var problem = ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.AllocationExceeded, problem.Code);
        var violation = Assert.Single(problem.Violations!);
        Assert.Equal((21m, 20m), (violation.Current!.Value, violation.Limit!.Value));
        Assert.Equal((20, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        Assert.Equal(PoStatus.Confirmed, (await H.GetPoAsync(po.Id)).Status);
    }

    [Fact]
    public async Task Alokasi_tanpa_baris_saldo_stok_ditolak_dan_pada_PO_Draft_Completed_atau_Cancelled_ditolak()
    {
        var w = await H.NewPoWorldAsync();
        var confirmed = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        Assert.Equal(ProblemCodes.AllocationExceeded,
            ProblemAssert.Is(await H.Api.PostAsync(Allocate(confirmed.Lines[0].Id), new AllocateRequest(1, null), H.AdminToken),
                HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        ProblemAssert.Is(await H.Api.PostAsync(Allocate(draft.Lines[0].Id), new AllocateRequest(1, null), H.AdminToken),
            HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        var cancelled = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        (await H.Api.PostAsync($"/api/v1/purchase-orders/{cancelled.Id}/cancel", new Gms.Shared.Common.ReasonRequest("batal", cancelled.RowVersion), H.AdminToken)).Ok();
        ProblemAssert.Is(await H.Api.PostAsync(Allocate(cancelled.Lines[0].Id), new AllocateRequest(1, null), H.AdminToken),
            HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        ProblemAssert.Is(await H.Api.PostAsync(Allocate(Guid.NewGuid()), new AllocateRequest(1, null), H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Pelepasan_sebagian_dan_seluruhnya_mengembalikan_stok_dan_sisa_rencana_dan_tidak_boleh_melebihi_alokasi()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var lineId = po.Lines[0].Id;
        (await H.Api.PostAsync(Allocate(lineId), new AllocateRequest(60, null), H.AdminToken)).Ok();

        var tooMuch = await H.Api.PostAsync(Release(lineId), new ReleaseAllocationRequest(61, null), H.AdminToken);
        Assert.Equal(ProblemCodes.AllocationExceeded, ProblemAssert.Is(tooMuch, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        var partial = (await H.Api.PostAsync(Release(lineId), new ReleaseAllocationRequest(25, "salah pilih"), H.AdminToken)).Ok().As<PoLineDto>();
        Assert.Equal((35, 65), (partial.AllocatedQty, partial.RemainingPlanQty));
        Assert.Equal((100, 35), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));

        var all = (await H.Api.PostAsync(Release(lineId), new ReleaseAllocationRequest(35, null), H.AdminToken)).Ok().As<PoLineDto>();
        Assert.Equal((0, 100), (all.AllocatedQty, all.RemainingPlanQty));
        Assert.Equal((100, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);

        ProblemAssert.Is(await H.Api.PostAsync(Release(lineId), new ReleaseAllocationRequest(0, null), H.AdminToken),
            HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);

        var entries = (await H.Api.GetAsync($"/api/v1/purchase-orders/{po.Id}/allocations", H.AdminToken)).Ok().As<List<AllocationEntryDto>>();
        Assert.Equal([AllocationKind.Release, AllocationKind.Release, AllocationKind.Allocate], entries.Select(e => e.Kind));
        Assert.True(entries.SequenceEqual(entries.OrderByDescending(e => e.Id)), "terbaru lebih dulu");
    }

    [Fact]
    public async Task Sisa_rencana_menjadi_nol_membatasi_alokasi_berikutnya()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 500);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 40)]);
        (await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(40, null), H.AdminToken)).Ok();

        var more = await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(1, null), H.AdminToken);

        Assert.Equal(0, ProblemAssert.Is(more, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Violations![0].Limit);
        Assert.Contains("Sisa Rencana", (await AvailabilityAsync(po.Lines[0].Id)).LimitReason);
    }

    // ===== Hak akses =====

    [Fact]
    public async Task Alokasi_dan_pelepasan_dilindungi_permission_terpisah()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var lineId = po.Lines[0].Id;
        var poOnly = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null,
            Permissions.CustomerPO.View, Permissions.CustomerPO.Alokasi.View)).Id);
        var allocator = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null,
            Permissions.CustomerPO.View, Permissions.CustomerPO.Alokasi.View, Permissions.CustomerPO.Alokasi.Allocate)).Id);

        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/po-lines/{lineId}/stock-availability", poOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/purchase-orders/{po.Id}/allocations", poOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.Api.GetAsync($"/api/v1/po-lines/{lineId}/stock-availability", viewer.Token)).IsSuccess);
        ProblemAssert.Is(await H.Api.PostAsync(Allocate(lineId), new AllocateRequest(1, null), viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);

        (await H.Api.PostAsync(Allocate(lineId), new AllocateRequest(5, null), allocator.Token)).Ok();
        ProblemAssert.Is(await H.Api.PostAsync(Release(lineId), new ReleaseAllocationRequest(1, null), allocator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
    }

    [Fact]
    public async Task Alokasi_dan_pelepasan_tercatat_di_audit()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        (await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(10, null), H.AdminToken)).Ok();
        (await H.Api.PostAsync(Release(po.Lines[0].Id), new ReleaseAllocationRequest(4, null), H.AdminToken)).Ok();

        await using var db = H.CreateDbContext();
        var events = await db.AuditLogs.Where(a => a.TargetId == po.Lines[0].Id.ToString()).Select(a => a.EventType).ToListAsync();
        Assert.Contains("PoAllocated", events);
        Assert.Contains("PoAllocationReleased", events);
        Assert.Contains("PoStatusChanged", await db.AuditLogs.Where(a => a.TargetId == po.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    // ===== KONKURENSI =====

    [Fact]
    public async Task Dua_PO_berebut_stok_yang_sama_tepat_satu_berhasil_dan_yang_lain_ditolak_409()
    {
        for (var round = 0; round < 6; round++)
        {
            var w = await H.NewPoWorldAsync();
            await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 10);
            var a = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
            var b = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
            var gate = new TaskCompletionSource();

            Task<ApiResponse> Go(Guid lineId) => Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync(Allocate(lineId), new AllocateRequest(8, null), H.AdminToken);
            });

            var tasks = new[] { Go(a.Lines[0].Id), Go(b.Lines[0].Id) };
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            var loser = results.Single(r => !r.IsSuccess);
            Assert.Equal(ProblemCodes.AllocationExceeded, ProblemAssert.Is(loser, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
            Assert.Equal((10, 8), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
            await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
        }
    }

    [Fact]
    public async Task Tiga_puluh_alokasi_paralel_pada_stok_20_tepat_20_berhasil_tanpa_500_dan_stok_konsisten()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 20);
        var lineIds = new List<Guid>();
        for (var i = 0; i < 15; i++)
        {
            var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
            lineIds.Add(po.Lines[0].Id);
        }

        var requests = lineIds.Concat(lineIds).ToList(); // 30 permintaan qty 1, dua per baris
        var gate = new TaskCompletionSource();
        var tasks = requests.Select(id => Task.Run(async () =>
        {
            await gate.Task;
            return await H.Api.PostAsync(Allocate(id), new AllocateRequest(1, null), H.AdminToken);
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(20, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => !r.IsSuccess), r => Assert.Equal(HttpStatusCode.Conflict, r.Status));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        Assert.Equal((20, 20), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
    }

    [Fact]
    public async Task Alokasi_dan_pelepasan_bersamaan_pada_baris_yang_sama_tetap_konsisten()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var lineId = po.Lines[0].Id;
        (await H.Api.PostAsync(Allocate(lineId), new AllocateRequest(50, null), H.AdminToken)).Ok();

        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            await gate.Task;
            return i % 2 == 0
                ? await H.Api.PostAsync(Allocate(lineId), new AllocateRequest(3, null), H.AdminToken)
                : await H.Api.PostAsync(Release(lineId), new ReleaseAllocationRequest(2, null), H.AdminToken);
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
        var line = (await H.GetPoAsync(po.Id)).Lines[0];
        Assert.Equal(line.AllocatedQty, (await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id)).Allocated);
        Assert.True(line.AllocatedQty + line.DeliveredQty + line.InProductionQty <= line.Qty);
    }

    [Fact]
    public async Task Alokasi_bersamaan_dengan_pembatalan_PO_tidak_meninggalkan_stok_tertahan()
    {
        for (var round = 0; round < 4; round++)
        {
            var w = await H.NewPoWorldAsync();
            await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 50);
            var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
            (await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(10, null), H.AdminToken)).Ok();
            var current = await H.GetPoAsync(po.Id);
            var gate = new TaskCompletionSource();

            var allocate = Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(5, null), H.AdminToken);
            });
            var cancel = Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel",
                    new Gms.Shared.Common.ReasonRequest("batal bersamaan", current.RowVersion), H.AdminToken);
            });
            gate.SetResult();
            var results = await Task.WhenAll(allocate, cancel);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            var after = await H.GetPoAsync(po.Id);
            if (after.Status == PoStatus.Cancelled)
            {
                Assert.Equal(0, after.Lines[0].AllocatedQty);
                Assert.Equal((50, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
            }

            await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
        }
    }

    [Fact]
    public async Task Revisi_bersamaan_dengan_alokasi_tidak_pernah_menurunkan_qty_di_bawah_alokasi()
    {
        for (var round = 0; round < 5; round++)
        {
            var w = await H.NewPoWorldAsync();
            await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
            var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
            var gate = new TaskCompletionSource();

            var allocate = Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync(Allocate(po.Lines[0].Id), new AllocateRequest(80, null), H.AdminToken);
            });
            var revise = Task.Run(async () =>
            {
                await gate.Task;
                return await H.PutPoAsync(po.Id, po.ToRequest([po.Lines[0].AsLine(qty: 40)]));
            });
            gate.SetResult();
            var results = await Task.WhenAll(allocate, revise);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            var line = (await H.GetPoAsync(po.Id)).Lines[0];
            Assert.True(line.Qty >= line.DeliveredQty + line.AllocatedQty + line.InProductionQty, $"qty {line.Qty} < alokasi {line.AllocatedQty}");
            await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
        }
    }
}
