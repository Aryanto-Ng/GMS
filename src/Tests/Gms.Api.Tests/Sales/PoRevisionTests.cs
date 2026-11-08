using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Sales;

/// <summary>Revisi PO (FSD Customer PO 4.5, AC-PO-19 s.d. AC-PO-24).</summary>
[Collection(ApiCollection.Name)]
public sealed class PoRevisionTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    [Fact]
    public async Task Draft_dapat_diubah_bebas_termasuk_mengganti_dan_menghapus_baris_dan_customer()
    {
        var w = await H.NewPoWorldAsync();
        var otherCustomer = await H.NewCustomerAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 50)], confirm: false);

        var request = po.ToRequest([w.Line(w.Medium, 70)]) with { CustomerId = otherCustomer.Id, PoDate = Today(-2) };
        var updated = (await H.PutPoAsync(po.Id, request)).Ok().As<PoDto>();

        Assert.Equal(otherCustomer.Id, updated.CustomerId);
        Assert.Equal(Today(-2), updated.PoDate);
        Assert.Single(updated.Lines);
        Assert.Equal(70, updated.Lines[0].Qty);
        Assert.Equal(PoStatus.Draft, updated.Status);
    }

    [Fact]
    public async Task Setelah_dikonfirmasi_customer_dan_tanggal_PO_terkunci_tetapi_nomor_catatan_dan_tanggal_kirim_dapat_diubah()
    {
        var w = await H.NewPoWorldAsync();
        var other = await H.NewCustomerAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);

        var locked = await H.PutPoAsync(po.Id, po.ToRequest() with { CustomerId = other.Id, PoDate = Today(-9) });
        var problem = ProblemAssert.Is(locked, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains("customerId", problem.Errors!.Keys);
        Assert.Contains("poDate", problem.Errors.Keys);

        var newDate = Today(60);
        var updated = (await H.PutPoAsync(po.Id, po.ToRequest([po.Lines[0].AsLine(delivery: newDate)], "PO/REV/1", "catatan revisi"))).Ok().As<PoDto>();
        Assert.Equal("PO/REV/1", updated.CustomerPoNumber);
        Assert.Equal("catatan revisi", updated.Notes);
        Assert.Equal(newDate, updated.Lines[0].DeliveryDate);
    }

    [Fact]
    public async Task Menaikkan_qty_dan_menambah_baris_baru_diizinkan_pada_PO_terkonfirmasi()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);

        var request = po.ToRequest([po.Lines[0].AsLine(qty: 150), w.Line(w.Medium, 40)]);
        var updated = (await H.PutPoAsync(po.Id, request)).Ok().As<PoDto>();

        Assert.Equal(2, updated.Lines.Count);
        Assert.Equal(190, updated.TotalQty);
        Assert.Equal(150, updated.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Qty);
        Assert.Equal(190, updated.RemainingPlanQty);
    }

    [Fact]
    public async Task Qty_tidak_boleh_turun_di_bawah_komitmen_dan_pesan_menyebut_komponen_penahan()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var lineId = po.Lines[0].Id;
        (await H.Api.PostAsync($"/api/v1/po-lines/{lineId}/allocate", new AllocateRequest(30, null), H.AdminToken)).Ok();
        await H.UpdateLineAsync(lineId, l => { l.DeliveredQty = 10; l.InProductionQty = 15; });
        var current = await H.GetPoAsync(po.Id);

        var tooLow = await H.PutPoAsync(po.Id, current.ToRequest([current.Lines[0].AsLine(qty: 54)]));
        var problem = ProblemAssert.Is(tooLow, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.PoQtyBelowCommitted, problem.Code);
        var violation = Assert.Single(problem.Violations!);
        Assert.Equal((54m, 55m), (violation.Current!.Value, violation.Limit!.Value));
        Assert.Contains("Terkirim 10", violation.Message);
        Assert.Contains("Dialokasikan 30", violation.Message);
        Assert.Contains("Dalam Produksi 15", violation.Message);

        var exact = (await H.PutPoAsync(po.Id, current.ToRequest([current.Lines[0].AsLine(qty: 55)]))).Ok().As<PoDto>();
        Assert.Equal(0, exact.Lines[0].RemainingPlanQty);
        Assert.Equal(55, exact.Lines[0].MinAllowedQty);
    }

    [Fact]
    public async Task FG_Color_Size_baris_yang_sudah_terpakai_terkunci_dan_baris_belum_terpakai_bebas()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 100)]);
        var small = po.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id);
        var medium = po.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id);
        (await H.Api.PostAsync($"/api/v1/po-lines/{small.Id}/allocate", new AllocateRequest(10, null), H.AdminToken)).Ok();
        var current = await H.GetPoAsync(po.Id);
        Assert.True(current.Lines.Single(l => l.Id == small.Id).IsLocked);
        Assert.False(current.Lines.Single(l => l.Id == medium.Id).IsLocked);

        var swapLocked = new SavePoLine(small.Id, small.FgId, small.ColorSize.ColorId, w.Medium.Id, small.DeliveryDate, 100, null);
        var rejected = await H.PutPoAsync(po.Id, current.ToRequest([swapLocked, medium.AsLine()]));
        Assert.Contains("lines[0].fgId", ProblemAssert.Is(rejected, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var otherColor = await H.NewColorAsync();
        var freeChange = new SavePoLine(medium.Id, medium.FgId, otherColor.Id, w.Medium.Id, medium.DeliveryDate, 100, null);
        var ok = (await H.PutPoAsync(po.Id, current.ToRequest([small.AsLine(), freeChange]))).Ok().As<PoDto>();
        Assert.Contains(ok.Lines, l => l.ColorSize.ColorId == otherColor.Id);
    }

    [Fact]
    public async Task Baris_hanya_dapat_dihapus_bila_belum_punya_komitmen_atau_riwayat()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 100)]);
        var small = po.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id);
        var medium = po.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id);
        (await H.Api.PostAsync($"/api/v1/po-lines/{small.Id}/allocate", new AllocateRequest(5, null), H.AdminToken)).Ok();
        var current = await H.GetPoAsync(po.Id);

        var blocked = await H.PutPoAsync(po.Id, current.ToRequest([medium.AsLine()]));
        Assert.Equal(ProblemCodes.PoLineInUse, ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);

        var removed = (await H.PutPoAsync(po.Id, current.ToRequest([small.AsLine()]))).Ok().As<PoDto>();
        Assert.Single(removed.Lines);

        // Setelah alokasi dilepas, riwayat alokasi tetap ada sehingga baris tetap tidak boleh dihapus.
        (await H.Api.PostAsync($"/api/v1/po-lines/{small.Id}/release-allocation", new ReleaseAllocationRequest(5, null), H.AdminToken)).Ok();
        var afterRelease = await H.GetPoAsync(po.Id);
        Assert.True(afterRelease.Lines[0].IsLocked, "riwayat alokasi mengunci FG/Color/Size");
        var emptied = await H.PutPoAsync(po.Id, afterRelease.ToRequest([]));
        Assert.Contains("lines", ProblemAssert.Is(emptied, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Fact]
    public async Task Menurunkan_qty_hingga_semua_baris_terkirim_penuh_menutup_PO_otomatis_dan_PO_selesai_terkunci()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 10);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 50)]);
        await H.UpdateLineAsync(po.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Id, l => l.DeliveredQty = 40);
        await H.UpdateLineAsync(po.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id).Id, l => l.DeliveredQty = 50);
        var current = await H.GetPoAsync(po.Id);

        var completed = (await H.PutPoAsync(po.Id, current.ToRequest(
            [current.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).AsLine(qty: 40), current.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id).AsLine()])))
            .Ok().As<PoDto>();

        Assert.Equal(PoStatus.Completed, completed.Status);
        Assert.Empty(completed.AllowedActions);

        ProblemAssert.Is(await H.PutPoAsync(po.Id, completed.ToRequest()), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        var allocate = await H.Api.PostAsync($"/api/v1/po-lines/{completed.Lines[0].Id}/allocate", new AllocateRequest(1, null), H.AdminToken);
        ProblemAssert.Is(allocate, HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Dua_penyunting_dengan_rowVersion_sama_penyimpan_kedua_mendapat_409()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);

        (await H.PutPoAsync(po.Id, po.ToRequest(notes: "penyunting A"))).Ok();
        var stale = await H.PutPoAsync(po.Id, po.ToRequest(notes: "penyunting B"));

        ProblemAssert.Is(stale, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Revisi_menolak_baris_asing_validasi_baris_baru_dan_duplikat_nomor_PO_customer()
    {
        var w = await H.NewPoWorldAsync();
        var number = "PO/" + Guid.NewGuid().ToString("N")[..8];
        await H.NewPoAsync(w, [w.Line(w.Small, 10)], customerPoNumber: number);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);

        var foreignLine = po.ToRequest([w.Line(w.Medium, 10, id: Guid.NewGuid())]);
        Assert.Contains("lines[0].id", ProblemAssert.Is(await H.PutPoAsync(po.Id, foreignLine), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var zeroQty = po.ToRequest([po.Lines[0].AsLine(), w.Line(w.Medium, 0)]);
        Assert.Contains("lines[1].qty", ProblemAssert.Is(await H.PutPoAsync(po.Id, zeroQty), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        ProblemAssert.Is(await H.PutPoAsync(po.Id, po.ToRequest(customerPoNumber: number)), HttpStatusCode.Conflict, ProblemTypes.Duplicate);
    }

    [Fact]
    public async Task Revisi_dicatat_di_audit_dan_PO_dibatalkan_tidak_dapat_direvisi()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        var revised = (await H.PutPoAsync(po.Id, po.ToRequest(notes: "revisi audit"))).Ok().As<PoDto>();

        await using (var db = H.CreateDbContext())
        {
            Assert.Contains("PoUpdated", await db.AuditLogs.Where(a => a.TargetId == po.Id.ToString()).Select(a => a.EventType).ToListAsync());
        }

        (await H.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel", new Gms.Shared.Common.ReasonRequest("batal", revised.RowVersion), H.AdminToken)).Ok();
        var cancelled = await H.GetPoAsync(po.Id);
        ProblemAssert.Is(await H.PutPoAsync(po.Id, cancelled.ToRequest()), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }
}
