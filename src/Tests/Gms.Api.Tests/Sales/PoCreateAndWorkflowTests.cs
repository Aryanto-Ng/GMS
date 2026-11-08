using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Domain.Production;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Sales;

/// <summary>Pembuatan PO dan alur status Draft → Confirmed → Cancelled (FSD Customer PO 4.1, 4.3, 4.6).</summary>
[Collection(ApiCollection.Name)]
public sealed class PoCreateAndWorkflowTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private const string Base = "/api/v1/purchase-orders";

    // ===== Membuat =====

    [Fact]
    public async Task Buat_PO_Draft_dengan_kode_otomatis_dan_nomor_PO_customer_boleh_kosong()
    {
        var w = await H.NewPoWorldAsync();

        var response = await H.PostPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 50)]);

        Assert.Equal(HttpStatusCode.Created, response.Status);
        var po = response.As<PoDto>();
        Assert.Matches(@"^PO-\d{6}-\d{4}$", po.Code);
        Assert.Equal(PoStatus.Draft, po.Status);
        Assert.Null(po.CustomerPoNumber);
        Assert.Equal(150, po.TotalQty);
        Assert.Equal(150, po.RemainingPlanQty);
        Assert.All(po.Lines, l => Assert.Equal((0, 0, 0), (l.DeliveredQty, l.AllocatedQty, l.InProductionQty)));
        Assert.Equal(w.Customer.Name, po.CustomerName);
        Assert.Equal(["Size uji", "Size uji"], po.Lines.Select(l => l.ColorSize.SizeName));
        Assert.Contains(PoActions.Confirm, po.AllowedActions);
    }

    [Fact]
    public async Task Buat_PO_langsung_dikonfirmasi_dan_kode_bulanan_berurutan()
    {
        var w = await H.NewPoWorldAsync();

        var first = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: true);
        var second = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);

        Assert.Equal(PoStatus.Confirmed, first.Status);
        Assert.Equal(PoStatus.Draft, second.Status);
        var n1 = int.Parse(first.Code[^4..]);
        var n2 = int.Parse(second.Code[^4..]);
        Assert.Equal(first.Code[..^4], second.Code[..^4]);
        Assert.Equal(1, n2 - n1);
    }

    [Fact]
    public async Task Pembuatan_bersamaan_menghasilkan_kode_PO_unik_tanpa_lubang()
    {
        var w = await H.NewPoWorldAsync();

        var created = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => H.NewPoAsync(w, [w.Line(w.Small, 5)], confirm: false)));

        var numbers = created.Select(p => int.Parse(p.Code[^4..])).OrderBy(n => n).ToList();
        Assert.Equal(10, numbers.Distinct().Count());
        Assert.Equal(9, numbers[^1] - numbers[0]);
    }

    [Fact]
    public async Task Konfirmasi_saat_membuat_tanpa_baris_ditolak_tetapi_Draft_tanpa_baris_diizinkan()
    {
        var w = await H.NewPoWorldAsync();

        var confirmEmpty = await H.PostPoAsync(w, [], confirm: true);
        Assert.Contains("lines", ProblemAssert.Is(confirmEmpty, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var draftEmpty = (await H.PostPoAsync(w, [], confirm: false)).Ok().As<PoDto>();
        Assert.Empty(draftEmpty.Lines);
        Assert.DoesNotContain(PoActions.Confirm, draftEmpty.AllowedActions);
    }

    [Fact]
    public async Task Customer_harus_ada_dan_aktif()
    {
        var w = await H.NewPoWorldAsync();
        var missing = await H.Api.PostAsync(Base,
            new SavePoRequest(Guid.NewGuid(), Today(), null, null, [w.Line(w.Small, 10)]), H.AdminToken);
        Assert.Contains("customerId", ProblemAssert.Is(missing, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var customer = await H.NewCustomerAsync();
        (await H.Api.PostAsync($"/api/v1/master/customers/{customer.Id}/deactivate?rowVersion={customer.RowVersion}", null, H.AdminToken)).Ok();
        var inactive = await H.Api.PostAsync(Base,
            new SavePoRequest(customer.Id, Today(), null, null, [w.Line(w.Small, 10)]), H.AdminToken);
        Assert.Contains("customerId", ProblemAssert.Is(inactive, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Theory]
    [InlineData(0, "qty")]
    [InlineData(-3, "qty")]
    [InlineData(1_000_001, "qty")]
    public async Task Qty_baris_harus_antara_1_dan_1_juta(int qty, string field)
    {
        var w = await H.NewPoWorldAsync();

        var response = await H.PostPoAsync(w, [w.Line(w.Small, qty)]);

        var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
        Assert.Contains($"lines[0].{field}", problem.Errors!.Keys);
    }

    [Fact]
    public async Task Baris_menolak_FG_Color_Size_nonaktif_size_di_luar_Size_Berlaku_dan_kombinasi_ganda()
    {
        var w = await H.NewPoWorldAsync();
        var foreignSize = await H.NewSizeAsync();
        var inactiveColor = await H.NewColorAsync();
        (await H.Api.PostAsync($"/api/v1/master/colors/{inactiveColor.Id}/deactivate?rowVersion={inactiveColor.RowVersion}", null, H.AdminToken)).Ok();

        var notApplicable = await H.PostPoAsync(w, [w.Line(foreignSize, 10)]);
        Assert.Contains("lines[0].sizeId", ProblemAssert.Is(notApplicable, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var badColor = await H.PostPoAsync(w, [w.Line(w.Small, 10) with { ColorId = inactiveColor.Id }]);
        Assert.Contains("lines[0].colorId", ProblemAssert.Is(badColor, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var badFg = await H.PostPoAsync(w, [w.Line(w.Small, 10) with { FgId = Guid.NewGuid() }]);
        Assert.Contains("lines[0].fgId", ProblemAssert.Is(badFg, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var duplicate = await H.PostPoAsync(w, [w.Line(w.Small, 10), w.Line(w.Small, 20)]);
        Assert.Contains("lines[1].fgId", ProblemAssert.Is(duplicate, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        // Tanggal kirim berbeda = baris berbeda (pengiriman bertahap), diizinkan.
        (await H.PostPoAsync(w, [w.Line(w.Small, 10, 30), w.Line(w.Small, 20, 45)])).Ok();
    }

    [Fact]
    public async Task Tanggal_kirim_wajib_dan_FG_tanpa_syarat_siap_produksi_diizinkan_saat_PO()
    {
        var w = await H.NewPoWorldAsync();
        var notReady = await H.NewReadyFgAsync(w.Customer.Id, fillTariffs: false);
        Assert.False(notReady.Fg.IsReady);

        var noDate = await H.PostPoAsync(w, [w.Line(w.Small, 10) with { DeliveryDate = default }]);
        Assert.Contains("lines[0].deliveryDate", ProblemAssert.Is(noDate, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var line = new SavePoLine(null, notReady.Fg.Id, w.Color.Id, notReady.Small.Id, Today(10), 5, null);
        (await H.PostPoAsync(w, [line])).Ok("FG belum siap tetap boleh di PO (pemeriksaan saat rilis WO)");
    }

    // ===== Nomor PO customer =====

    [Fact]
    public async Task Nomor_PO_customer_unik_per_customer_tanpa_peka_huruf_dan_bebas_dipakai_ulang_setelah_batal()
    {
        var w = await H.NewPoWorldAsync();
        var number = "PO/" + Guid.NewGuid().ToString("N")[..8];
        var first = await H.NewPoAsync(w, [w.Line(w.Small, 10)], customerPoNumber: number);

        var clash = await H.PostPoAsync(w, [w.Line(w.Small, 10)], customerPoNumber: number.ToLowerInvariant());
        var problem = ProblemAssert.Is(clash, HttpStatusCode.Conflict, ProblemTypes.Duplicate);
        Assert.Contains(first.Code, problem.Detail);

        var other = await H.NewPoWorldAsync();
        (await H.PostPoAsync(other, [other.Line(other.Small, 10)], customerPoNumber: number)).Ok("customer lain boleh nomor sama");

        (await H.Api.PostAsync($"{Base}/{first.Id}/cancel", new Gms.Shared.Common.ReasonRequest("batal", first.RowVersion), H.AdminToken)).Ok();
        (await H.PostPoAsync(w, [w.Line(w.Small, 10)], customerPoNumber: number)).Ok("nomor dari PO batal boleh dipakai ulang");
    }

    [Fact]
    public async Task Nomor_PO_customer_dapat_diisi_kemudian_dan_menolak_duplikat_serta_status_selesai()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        var taken = "PO/" + Guid.NewGuid().ToString("N")[..8];
        await H.NewPoAsync(w, [w.Line(w.Small, 10)], customerPoNumber: taken);

        var updated = (await H.Api.PutAsync($"{Base}/{po.Id}/customer-po-number",
            new SetCustomerPoNumberRequest("  PO/BARU/1  ", po.RowVersion), H.AdminToken)).Ok().As<PoDto>();
        Assert.Equal("PO/BARU/1", updated.CustomerPoNumber);

        var clash = await H.Api.PutAsync($"{Base}/{po.Id}/customer-po-number", new SetCustomerPoNumberRequest(taken, updated.RowVersion), H.AdminToken);
        ProblemAssert.Is(clash, HttpStatusCode.Conflict, ProblemTypes.Duplicate);

        var cleared = (await H.Api.PutAsync($"{Base}/{po.Id}/customer-po-number", new SetCustomerPoNumberRequest("  ", updated.RowVersion), H.AdminToken))
            .Ok().As<PoDto>();
        Assert.Null(cleared.CustomerPoNumber);

        var stale = await H.Api.PutAsync($"{Base}/{po.Id}/customer-po-number", new SetCustomerPoNumberRequest("X", po.RowVersion), H.AdminToken);
        ProblemAssert.Is(stale, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);

        (await H.Api.PostAsync($"{Base}/{po.Id}/cancel", new Gms.Shared.Common.ReasonRequest("batal", cleared.RowVersion), H.AdminToken)).Ok();
        var onCancelled = await H.Api.PutAsync($"{Base}/{po.Id}/customer-po-number", new SetCustomerPoNumberRequest("Y", 0), H.AdminToken);
        Assert.True(onCancelled.Status is HttpStatusCode.Conflict, onCancelled.Body);
    }

    // ===== Konfirmasi =====

    [Fact]
    public async Task Konfirmasi_Draft_mengubah_status_dan_tidak_membutuhkan_nomor_PO_customer()
    {
        var w = await H.NewPoWorldAsync();
        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);

        var confirmed = (await H.Api.PostAsync($"{Base}/{draft.Id}/confirm?rowVersion={draft.RowVersion}", null, H.AdminToken)).Ok().As<PoDto>();

        Assert.Equal(PoStatus.Confirmed, confirmed.Status);
        Assert.Null(confirmed.CustomerPoNumber);
        Assert.DoesNotContain(PoActions.Confirm, confirmed.AllowedActions);
        Assert.Contains(PoActions.Allocate, confirmed.AllowedActions);
    }

    [Fact]
    public async Task Konfirmasi_ditolak_tanpa_baris_dua_kali_atau_rowVersion_usang()
    {
        var w = await H.NewPoWorldAsync();
        var empty = (await H.PostPoAsync(w, [])).Ok().As<PoDto>();
        Assert.Contains("lines", ProblemAssert.Is(await H.Api.PostAsync($"{Base}/{empty.Id}/confirm?rowVersion={empty.RowVersion}", null, H.AdminToken),
            HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        ProblemAssert.Is(await H.Api.PostAsync($"{Base}/{po.Id}/confirm?rowVersion={po.RowVersion + 5}", null, H.AdminToken),
            HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);

        var confirmed = (await H.Api.PostAsync($"{Base}/{po.Id}/confirm?rowVersion={po.RowVersion}", null, H.AdminToken)).Ok().As<PoDto>();
        ProblemAssert.Is(await H.Api.PostAsync($"{Base}/{po.Id}/confirm?rowVersion={confirmed.RowVersion}", null, H.AdminToken),
            HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Pembatalan =====

    [Fact]
    public async Task Batal_wajib_alasan_dan_Draft_dapat_dibatalkan_tanpa_efek_samping()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);

        var noReason = await H.Api.PostAsync($"{Base}/{po.Id}/cancel", new Gms.Shared.Common.ReasonRequest("  ", po.RowVersion), H.AdminToken);
        Assert.Contains("reason", ProblemAssert.Is(noReason, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var cancelled = (await H.Api.PostAsync($"{Base}/{po.Id}/cancel", new Gms.Shared.Common.ReasonRequest("Customer membatalkan", po.RowVersion), H.AdminToken))
            .Ok().As<PoDto>();
        Assert.Equal(PoStatus.Cancelled, cancelled.Status);
        Assert.Equal("Customer membatalkan", cancelled.CancelReason);
        Assert.Empty(cancelled.AllowedActions);

        var again = await H.Api.PostAsync($"{Base}/{po.Id}/cancel", new Gms.Shared.Common.ReasonRequest("lagi", cancelled.RowVersion), H.AdminToken);
        ProblemAssert.Is(again, HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    [Fact]
    public async Task Batal_melepas_seluruh_alokasi_aktif_dan_stok_kembali_tersedia()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 50);
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Medium.Id, 30);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 100), w.Line(w.Medium, 100)]);
        foreach (var (line, qty) in po.Lines.Zip([20, 25]))
        {
            (await H.Api.PostAsync($"/api/v1/po-lines/{line.Id}/allocate", new AllocateRequest(qty, null), H.AdminToken)).Ok();
        }

        var current = await H.GetPoAsync(po.Id);
        var cancelled = (await H.Api.PostAsync($"{Base}/{po.Id}/cancel", new Gms.Shared.Common.ReasonRequest("batal", current.RowVersion), H.AdminToken))
            .Ok().As<PoDto>();

        Assert.All(cancelled.Lines, l => Assert.Equal(0, l.AllocatedQty));
        Assert.Equal((50, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        Assert.Equal((30, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Medium.Id));
        await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);
        await H.AssertStockInvariantAsync(w.Fg.Fg.Id, w.Color.Id, w.Medium.Id);

        var entries = (await H.Api.GetAsync($"{Base}/{po.Id}/allocations", H.AdminToken)).Ok().As<List<AllocationEntryDto>>();
        Assert.Equal(2, entries.Count(e => e.Kind == AllocationKind.Release && e.Source == AllocationSource.PoCancel));
    }

    [Fact]
    public async Task Batal_ditolak_bila_sudah_ada_qty_terkirim_atau_WO_aktif_dan_diizinkan_setelah_WO_dibatalkan()
    {
        var w = await H.NewPoWorldAsync();
        var delivered = await H.NewPoAsync(w, [w.Line(w.Small, 100)]);
        await H.UpdateLineAsync(delivered.Lines[0].Id, l => l.DeliveredQty = 10);
        var current = await H.GetPoAsync(delivered.Id);
        var blockedByDelivery = await H.Api.PostAsync($"{Base}/{delivered.Id}/cancel", new Gms.Shared.Common.ReasonRequest("x", current.RowVersion), H.AdminToken);
        Assert.Equal("po.cancel-blocked", ProblemAssert.Is(blockedByDelivery, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        Assert.DoesNotContain(PoActions.Cancel, current.AllowedActions);

        var withWo = await H.NewPoAsync(w, [w.Line(w.Medium, 100)]);
        Guid woId;
        await using (var db = H.CreateDbContext())
        {
            var wo = new WorkOrder
            {
                Code = MasterHelpers.Code("WO-T-"), PoId = withWo.Id, FgId = w.Fg.Fg.Id, FgCode = w.Fg.Fg.Code, FgName = "FG",
                Status = WoStatus.Released, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.WorkOrders.Add(wo);
            await db.SaveChangesAsync();
            woId = wo.Id;
        }

        var woCurrent = await H.GetPoAsync(withWo.Id);
        var blockedByWo = await H.Api.PostAsync($"{Base}/{withWo.Id}/cancel", new Gms.Shared.Common.ReasonRequest("x", woCurrent.RowVersion), H.AdminToken);
        var problem = ProblemAssert.Is(blockedByWo, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Contains(problem.Violations!, v => v.Subject.StartsWith("WO "));
        Assert.DoesNotContain(PoActions.Cancel, woCurrent.AllowedActions);

        await using (var db = H.CreateDbContext())
        {
            await db.WorkOrders.Where(x => x.Id == woId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, WoStatus.Cancelled));
        }

        var after = await H.GetPoAsync(withWo.Id);
        Assert.Contains(PoActions.Cancel, after.AllowedActions);
        (await H.Api.PostAsync($"{Base}/{withWo.Id}/cancel", new Gms.Shared.Common.ReasonRequest("ok", after.RowVersion), H.AdminToken)).Ok();
    }

    // ===== Hak akses & audit =====

    [Fact]
    public async Task Hak_akses_PO_ditegakkan_per_aksi_dan_AllowedActions_mengikuti_permission()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        var viewOnly = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        var creator = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null,
            Permissions.CustomerPO.View, Permissions.CustomerPO.Create)).Id);
        var none = await H.CreateReadyUserAsync((await H.CreateRoleAsync()).Id);

        Assert.True((await H.Api.GetAsync(Base, viewOnly.Token)).IsSuccess);
        var asViewer = (await H.Api.GetAsync($"{Base}/{po.Id}", viewOnly.Token)).Ok().As<PoDto>();
        Assert.Empty(asViewer.AllowedActions);

        ProblemAssert.Is(await H.PostPoAsync(w, [w.Line(w.Small, 5)], token: viewOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.Equal(HttpStatusCode.Created, (await H.PostPoAsync(w, [w.Line(w.Small, 5)], token: creator.Token)).Status);
        ProblemAssert.Is(await H.Api.PostAsync($"{Base}/{po.Id}/confirm?rowVersion={po.RowVersion}", null, creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync(Base, none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync(Base), HttpStatusCode.Unauthorized, ProblemTypes.SessionExpired);
    }

    [Fact]
    public async Task Pembuatan_konfirmasi_dan_pembatalan_tercatat_di_audit()
    {
        var w = await H.NewPoWorldAsync();
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        var confirmed = (await H.Api.PostAsync($"{Base}/{po.Id}/confirm?rowVersion={po.RowVersion}", null, H.AdminToken)).Ok().As<PoDto>();
        (await H.Api.PostAsync($"{Base}/{po.Id}/cancel", new Gms.Shared.Common.ReasonRequest("uji audit", confirmed.RowVersion), H.AdminToken)).Ok();

        await using var db = H.CreateDbContext();
        var events = await db.AuditLogs.Where(a => a.TargetId == po.Id.ToString()).Select(a => a.EventType).ToListAsync();
        Assert.Contains("PoCreated", events);
        Assert.Contains("PoConfirmed", events);
        Assert.Contains("PoCancelled", events);
    }

    [Fact]
    public async Task PO_tidak_ada_memberi_404()
    {
        ProblemAssert.Is(await H.Api.GetAsync($"{Base}/{Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }
}
