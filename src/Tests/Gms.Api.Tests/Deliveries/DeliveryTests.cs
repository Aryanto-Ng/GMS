using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Deliveries;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Deliveries;

/// <summary>Pengiriman ke customer: surat jalan, konsumsi alokasi, status PO, void/koreksi, dan konkurensi (FSD Pengiriman).</summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveryTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    private async Task AssertConsistentAsync(PoWorld w, params Gms.Shared.Master.SizeDto[] sizes)
    {
        foreach (var size in sizes)
        {
            await H.AssertStockConsistentAsync(w.Fg.Fg.Id, w.Color.Id, size.Id);
        }
    }

    // ===== Simpan =====

    [Fact]
    public async Task Surat_jalan_mengurangi_stok_mengonsumsi_alokasi_menambah_Terkirim_dan_memindahkan_PO_ke_InProgress()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var s = po.PoLineId(w.Small);
        var m = po.PoLineId(w.Medium);
        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(po.Id)).Status); // alokasi manual sudah memicu

        var delivery = await H.DeliverAsync(po, (s, 30), (m, 20));

        Assert.Matches(@"^SJ-\d{6}-\d{4}$", delivery.Item.Code);
        Assert.Equal((50, DocStatus.Active, w.Customer.Name, po.Code), (delivery.Item.TotalQty, delivery.Item.Status, delivery.Item.CustomerName, delivery.Item.PoCode));
        Assert.Equal(2, delivery.Lines.Count);
        Assert.Equal(("PT Penerima", "B 1234 XY", "Pak Sopir"), (delivery.RecipientName, delivery.Vehicle, delivery.Driver));
        Assert.Equal((10, 30), await H.LineAsync(s));
        Assert.Equal((0, 20), await H.LineAsync(m));
        Assert.Equal((30, 10), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        Assert.Equal((10, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Medium.Id));
        await AssertConsistentAsync(w, w.Small, w.Medium);
        await H.AssertInProductionInvariantAsync(po.Id);

        var allocations = (await H.Api.GetAsync($"/api/v1/purchase-orders/{po.Id}/allocations", H.AdminToken)).Ok().As<List<AllocationEntryDto>>();
        Assert.Equal(2, allocations.Count(a => a.Kind == AllocationKind.Consume && a.Source == AllocationSource.Delivery));

        var ledger = (await H.Api.GetAsync($"/api/v1/stock/ledger?fgId={w.Fg.Fg.Id}&colorId={w.Color.Id}&sizeId={w.Small.Id}", H.AdminToken)).Ok().As<Gms.Shared.Inventory.StockLedgerResult>();
        Assert.Equal((StockLedgerKind.Delivery, -30), (ledger.Rows.Items[0].Kind, ledger.Rows.Items[0].Qty));

        await using var db = H.CreateDbContext();
        Assert.Contains("DeliveryCreated", await db.AuditLogs.Where(a => a.TargetId == delivery.Item.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task PO_Confirmed_tanpa_aktivitas_lain_menjadi_InProgress_saat_pengiriman_pertama()
    {
        var w = await H.NewPoWorldAsync();
        await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 10);
        var po = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        // Alokasi langsung lewat basis data agar PO tetap Confirmed.
        await using (var db = H.CreateDbContext())
        {
            await db.PoLines.Where(l => l.PoId == po.Id).ExecuteUpdateAsync(l => l.SetProperty(x => x.AllocatedQty, 10));
            await db.StockBalances.Where(x => x.FgId == w.Fg.Fg.Id).ExecuteUpdateAsync(x => x.SetProperty(b => b.Allocated, 10));
            await db.PoAllocations.AddAsync(new Gms.Domain.Sales.PoAllocation { PoLineId = po.Lines[0].Id, Kind = AllocationKind.Allocate, Qty = 10, Source = AllocationSource.Manual, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal(PoStatus.Confirmed, (await H.GetPoAsync(po.Id)).Status);
        await H.DeliverAsync(po, (po.Lines[0].Id, 4));

        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(po.Id)).Status);
    }

    [Fact]
    public async Task Pengiriman_bertahap_dan_PO_Completed_otomatis_saat_semua_baris_terkirim_penuh()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 40, poMedium: 20, allocSmall: 40, allocMedium: 20, stockSmall: 40, stockMedium: 20);
        var s = po.PoLineId(w.Small);
        var m = po.PoLineId(w.Medium);

        await H.DeliverAsync(po, (s, 25));
        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(po.Id)).Status);
        await H.DeliverAsync(po, (s, 15), (m, 10));
        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(po.Id)).Status);
        await H.DeliverAsync(po, (m, 10));

        var done = await H.GetPoAsync(po.Id);
        Assert.Equal(PoStatus.Completed, done.Status);
        Assert.Equal((60, 0, 0), (done.Lines.Sum(l => l.DeliveredQty), done.Lines.Sum(l => l.AllocatedQty), done.Lines.Sum(l => l.RemainingPlanQty)));
        await AssertConsistentAsync(w, w.Small, w.Medium);

        ProblemAssert.Is(await H.PostDeliveryAsync(po, [(s, 1)]), HttpStatusCode.Conflict, ProblemTypes.StateConflict); // PO Completed
        await using var db = H.CreateDbContext();
        Assert.Contains("PoStatusChanged", await db.AuditLogs.Where(a => a.TargetId == po.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Qty_melebihi_alokasi_ditolak_dengan_batas_terbaru_dan_tanpa_efek()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 100, allocSmall: 40);
        var s = po.PoLineId(w.Small);

        var overAlloc = await H.PostDeliveryAsync(po, [(s, 41)]);
        var problem = ProblemAssert.Is(overAlloc, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.DeliveryExceedsAllocation, problem.Code);
        Assert.Equal((41m, 40m), (problem.Violations![0].Current!.Value, problem.Violations[0].Limit!.Value));
        Assert.Contains("dialokasikan 40", problem.Violations[0].Message);
        Assert.Equal((40, 0), await H.LineAsync(s));
        Assert.Equal((60, 40), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
    }

    [Fact]
    public async Task Validasi_header_baris_dan_status_PO()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var s = po.PoLineId(w.Small);
        var otherPo = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        string[] Errors(ApiResponse r) => ProblemAssert.Is(r, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys.ToArray();

        Assert.Contains("lines", Errors(await H.PostDeliveryAsync(po, [])));
        Assert.Contains("lines", Errors(await H.PostDeliveryAsync(po, [(s, 0)])));
        Assert.Contains("lines[0].qty", Errors(await H.PostDeliveryAsync(po, [(s, -1)])));
        Assert.Contains("lines[1].poLineId", Errors(await H.PostDeliveryAsync(po, [(s, 1), (s, 2)])));
        Assert.Contains("lines[0].poLineId", Errors(await H.PostDeliveryAsync(po, [(otherPo.Lines[0].Id, 1)])));
        Assert.Contains("deliveryDate", Errors(await H.PostDeliveryAsync(po, [(s, 1)], date: Today(1))));
        Assert.Contains("recipientName", Errors(await H.PostDeliveryAsync(po, [(s, 1)], recipient: new string('x', 151))));
        ProblemAssert.Is(await H.PostDeliveryAsync(new PoDto(po.Id, "", null, Guid.Empty, "", default, PoStatus.Confirmed, null, null, [], 0, 0, 0, 0, 0, [], default, null, 0) with { Id = Guid.NewGuid() }, [(s, 1)]), HttpStatusCode.NotFound, ProblemTypes.NotFound);

        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        ProblemAssert.Is(await H.PostDeliveryAsync(draft, [(draft.Lines[0].Id, 1)]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        Assert.Equal(HttpStatusCode.Created, (await H.PostDeliveryAsync(po, [(s, 1)], date: Today(-3))).Status); // tanggal lampau diizinkan
    }

    // ===== Konteks =====

    [Fact]
    public async Task Konteks_menampilkan_batas_kirim_stok_tersedia_peringatan_dan_PO_tidak_layak_ditolak()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 100, poMedium: 50, allocSmall: 40, allocMedium: 0, stockSmall: 60, stockMedium: 30);
        await H.DeliverAsync(po, (po.PoLineId(w.Small), 10));

        var ctx = (await H.Api.GetAsync($"/api/v1/deliveries/context?poId={po.Id}", H.AdminToken)).Ok().As<DeliveryContextDto>();

        Assert.Equal((po.Code, w.Customer.Name, PoStatus.InProgress), (ctx.PoCode, ctx.CustomerName, ctx.PoStatus));
        var small = ctx.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id);
        Assert.Equal((100, 10, 30, 30, 20), (small.Qty, small.DeliveredQty, small.AllocatedQty, small.MaxDeliver, small.StockAvailable));
        var medium = ctx.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id);
        Assert.Equal((0, 30), (medium.MaxDeliver, medium.StockAvailable));
        Assert.Equal(2, ctx.Warnings.Count); // alokasi kurang pada kedua baris
        Assert.All(ctx.Lines, l => Assert.False(l.IsLate));

        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/deliveries/context?poId={draft.Id}", H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/deliveries/context?poId={Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    [Fact]
    public async Task Daftar_PO_layak_kirim_hanya_Confirmed_atau_InProgress_dengan_kebutuhan_tersisa_dan_dapat_dicari()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 10, poMedium: 5, allocSmall: 10, allocMedium: 5, stockSmall: 10, stockMedium: 5);
        var draft = await H.NewPoAsync(w, [w.Line(w.Small, 10)], confirm: false);
        var done = await H.NewPoAsync(w, [w.Line(w.Small, 10)]);
        await using (var db = H.CreateDbContext())
        {
            await db.PurchaseOrders.Where(p => p.Id == done.Id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, PoStatus.Completed));
        }

        var found = (await H.Api.GetAsync($"/api/v1/deliveries/eligible-pos?q={po.Code}", H.AdminToken)).Ok().As<List<EligiblePoItem>>();

        var item = Assert.Single(found);
        Assert.Equal((po.Id, 15, 15, w.Customer.Name), (item.PoId, item.OpenQty, item.AllocatedQty, item.CustomerName));
        var byCustomer = (await H.Api.GetAsync($"/api/v1/deliveries/eligible-pos?q={w.Customer.Name}", H.AdminToken)).Ok().As<List<EligiblePoItem>>();
        Assert.Contains(byCustomer, x => x.PoId == po.Id);
        Assert.DoesNotContain(byCustomer, x => x.PoId == draft.Id || x.PoId == done.Id);
    }

    // ===== Info non-qty =====

    [Fact]
    public async Task Info_non_qty_dapat_diubah_qty_tidak_dan_versi_usang_ditolak()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var delivery = await H.DeliverAsync(po, (po.PoLineId(w.Small), 10));
        string Url() => $"/api/v1/deliveries/{delivery.Item.Id}/info";

        var updated = (await H.Api.PutAsync(Url(), new UpdateDeliveryInfoRequest(Today(-1), "Penerima Baru", "D 5 AB", "Sopir 2", "catatan baru", delivery.RowVersion), H.AdminToken)).Ok().As<DeliveryDto>();

        Assert.Equal((Today(-1), "Penerima Baru", "D 5 AB", "Sopir 2", "catatan baru"), (updated.Item.DeliveryDate, updated.RecipientName, updated.Vehicle, updated.Driver, updated.Notes));
        Assert.Equal(10, updated.Lines.Single().Qty);
        Assert.Equal((30, 10), await H.LineAsync(po.PoLineId(w.Small)));

        ProblemAssert.Is(await H.Api.PutAsync(Url(), new UpdateDeliveryInfoRequest(Today(), "X", null, null, null, delivery.RowVersion), H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
        Assert.Contains("deliveryDate", ProblemAssert.Is(await H.Api.PutAsync(Url(), new UpdateDeliveryInfoRequest(Today(1), "X", null, null, null, updated.RowVersion), H.AdminToken), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        await H.VoidDeliveryAsync(delivery.Item.Id);
        var voided = await H.GetDeliveryAsync(delivery.Item.Id);
        ProblemAssert.Is(await H.Api.PutAsync(Url(), new UpdateDeliveryInfoRequest(Today(), "X", null, null, null, voided.RowVersion), H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.StateConflict);

        await using var db = H.CreateDbContext();
        Assert.Contains("DeliveryInfoUpdated", await db.AuditLogs.Where(a => a.TargetId == delivery.Item.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    // ===== Void =====

    [Fact]
    public async Task Void_membalik_semua_dampak_dan_PO_Completed_kembali_InProgress()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 40, poMedium: 20, allocSmall: 40, allocMedium: 20, stockSmall: 40, stockMedium: 20);
        var s = po.PoLineId(w.Small);
        var m = po.PoLineId(w.Medium);
        var delivery = await H.DeliverAsync(po, (s, 40), (m, 20));
        Assert.Equal(PoStatus.Completed, (await H.GetPoAsync(po.Id)).Status);

        Assert.Contains("reason", ProblemAssert.Is(await H.VoidDeliveryAsync(delivery.Item.Id, " "), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        var voided = (await H.VoidDeliveryAsync(delivery.Item.Id, "salah kirim")).Ok().As<DeliveryDto>();

        Assert.Equal((DocStatus.Voided, "salah kirim"), (voided.Item.Status, voided.VoidReason));
        Assert.DoesNotContain(DeliveryActions.Void, voided.AllowedActions);
        Assert.Equal((40, 0), await H.LineAsync(s));
        Assert.Equal((20, 0), await H.LineAsync(m));
        Assert.Equal((40, 40), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        Assert.Equal(PoStatus.InProgress, (await H.GetPoAsync(po.Id)).Status);
        await AssertConsistentAsync(w, w.Small, w.Medium);
        await H.AssertInProductionInvariantAsync(po.Id);

        var ledger = (await H.Api.GetAsync($"/api/v1/stock/ledger?fgId={w.Fg.Fg.Id}&colorId={w.Color.Id}&sizeId={w.Small.Id}", H.AdminToken)).Ok().As<Gms.Shared.Inventory.StockLedgerResult>();
        Assert.Equal(StockLedgerKind.DeliveryReversal, ledger.Rows.Items[0].Kind);
        var allocations = (await H.Api.GetAsync($"/api/v1/purchase-orders/{po.Id}/allocations", H.AdminToken)).Ok().As<List<AllocationEntryDto>>();
        Assert.Contains(allocations, a => a.Kind == AllocationKind.ReverseConsume && a.Source == AllocationSource.DeliveryVoid);

        ProblemAssert.Is(await H.VoidDeliveryAsync(delivery.Item.Id), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        (await H.PostDeliveryAsync(po, [(s, 40)])).Ok("kirim ulang setelah void");
    }

    [Fact]
    public async Task Pratinjau_void_menampilkan_dampak_dan_tidak_mengubah_apa_pun_dan_PO_batal_menolak_void()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 40, poMedium: 20, allocSmall: 40, allocMedium: 20, stockSmall: 40, stockMedium: 20);
        var s = po.PoLineId(w.Small);
        var delivery = await H.DeliverAsync(po, (s, 40));
        var before = await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id);

        var preview = (await H.Api.PostAsync($"/api/v1/deliveries/{delivery.Item.Id}/void/preview", new { }, H.AdminToken)).Ok().As<ImpactPreview>();

        Assert.True(preview.CanProceed);
        Assert.Contains(preview.Rows, r => r is { Metric: "Terkirim", Before: 40, After: 0 });
        Assert.Contains(preview.Rows, r => r is { Metric: "Stok Fisik", Before: 0, After: 40 });
        Assert.Equal(before, await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        Assert.Equal(DocStatus.Active, (await H.GetDeliveryAsync(delivery.Item.Id)).Item.Status);

        await using (var db = H.CreateDbContext())
        {
            await db.PurchaseOrders.Where(p => p.Id == po.Id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, PoStatus.Cancelled));
        }

        ProblemAssert.Is(await H.VoidDeliveryAsync(delivery.Item.Id), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
        Assert.DoesNotContain(DeliveryActions.Void, (await H.GetDeliveryAsync(delivery.Item.Id)).AllowedActions);
    }

    // ===== Koreksi =====

    [Fact]
    public async Task Koreksi_atomik_lolos_walau_seluruh_alokasi_sudah_dikonsumsi_dan_menyimpan_rujukan()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 100, allocSmall: 30, allocMedium: 0, stockSmall: 60);
        var s = po.PoLineId(w.Small);
        var delivery = await H.DeliverAsync(po, (s, 30)); // alokasi sekarang 0; void biasa + kirim ulang terpisah tidak diperlukan
        Assert.Equal((0, 30), await H.LineAsync(s));

        var corrected = (await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 30)], "ganti penerima", "PT Penerima Benar")).Ok().As<DeliveryDto>();

        Assert.NotEqual(delivery.Item.Code, corrected.Item.Code);
        Assert.Equal((delivery.Item.Id, "PT Penerima Benar"), (corrected.ReplacesId!.Value, corrected.RecipientName));
        var old = await H.GetDeliveryAsync(delivery.Item.Id);
        Assert.Equal((DocStatus.Voided, corrected.Item.Id), (old.Item.Status, old.ReplacedById!.Value));
        Assert.StartsWith("Dikoreksi:", old.VoidReason);
        Assert.Equal((0, 30), await H.LineAsync(s));
        Assert.Equal((30, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        await AssertConsistentAsync(w, w.Small);

        var smaller = (await H.CorrectDeliveryAsync(corrected.Item.Id, [(s, 20)], "kurangi")).Ok().As<DeliveryDto>();
        Assert.Equal((10, 20), await H.LineAsync(s)); // 10 kembali menjadi alokasi aktif
        Assert.Equal(20, smaller.Item.TotalQty);
        await using var db = H.CreateDbContext();
        Assert.Contains("DeliveryCorrected", await db.AuditLogs.Where(a => a.TargetId == delivery.Item.Id.ToString()).Select(a => a.EventType).ToListAsync());
    }

    [Fact]
    public async Task Koreksi_ditolak_bila_kondisi_akhir_melanggar_dan_pratinjau_menjelaskan_tanpa_menyimpan()
    {
        var (w, po) = await H.NewDeliverableAsync(poSmall: 100, allocSmall: 30, allocMedium: 0, stockSmall: 60);
        var s = po.PoLineId(w.Small);
        var delivery = await H.DeliverAsync(po, (s, 30));

        var tooMuch = await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 31)]);
        Assert.Equal(ProblemCodes.DeliveryExceedsAllocation, ProblemAssert.Is(tooMuch, HttpStatusCode.Conflict, ProblemTypes.RuleViolation).Code);
        var preview = (await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 31)], preview: true)).Ok().As<ImpactPreview>();
        Assert.False(preview.CanProceed);
        var okPreview = (await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 10)], preview: true)).Ok().As<ImpactPreview>();
        Assert.True(okPreview.CanProceed);
        Assert.Equal((0, 30), await H.LineAsync(s));
        Assert.Equal(DocStatus.Active, (await H.GetDeliveryAsync(delivery.Item.Id)).Item.Status);

        Assert.Contains("reason", ProblemAssert.Is(await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 5)], " "), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        Assert.Contains("lines", ProblemAssert.Is(await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 0)]), HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
        await H.VoidDeliveryAsync(delivery.Item.Id);
        ProblemAssert.Is(await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 5)]), HttpStatusCode.Conflict, ProblemTypes.StateConflict);
    }

    // ===== Integrasi dengan Serah Terima FG (E2) =====

    [Fact]
    public async Task Alur_nyata_produksi_serah_terima_pengiriman_dan_void_serah_terima_ditolak_karena_sudah_dikirim()
    {
        var s = await H.NewProdSetupAsync(poQty: 100, woQtySmall: 60, woQtyMedium: 40);
        await H.ProduceAsync(s, 30);
        var handover = await H.HandoverAsync(s, (s.LineS, 30)); // 30 teralokasi otomatis ke PO
        var poLine = s.Po.PoLineId(s.World.Small);

        var delivery = await H.DeliverAsync(s.Po, (poLine, 30));

        Assert.Equal((0, 30), await H.LineAsync(poLine));
        Assert.Equal((0, 0), await H.StockAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id));
        var blocked = await H.VoidHandoverAsync(handover.Id);
        var problem = ProblemAssert.Is(blocked, HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
        Assert.Equal(ProblemCodes.StockInsufficient, problem.Code);
        Assert.Contains("dikirim", problem.Violations![0].Message);

        await H.VoidDeliveryAsync(delivery.Item.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await H.VoidHandoverAsync(handover.Id)).Status); // setelah pengiriman dibatalkan, boleh
        await H.AssertStockConsistentAsync(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Small.Id);
        await H.AssertInProductionInvariantAsync(s.Po.Id);
    }

    // ===== Daftar dan permission =====

    [Fact]
    public async Task Daftar_menyaring_dan_permission_dipisah_untuk_lihat_buat_ubah_void_dan_koreksi()
    {
        var (w, po) = await H.NewDeliverableAsync();
        var s = po.PoLineId(w.Small);
        var a = await H.DeliverAsync(po, (s, 5));
        var b = await H.DeliverAsync(po, (s, 3));
        await H.VoidDeliveryAsync(b.Item.Id);
        var viewer = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.Pengiriman.View)).Id);
        var creator = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.Pengiriman.View, Permissions.Pengiriman.Create)).Id);
        var none = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);

        async Task<PagedResult<DeliveryListItem>> List(string q) =>
            (await H.Api.GetAsync($"/api/v1/deliveries?pageSize=50&poId={po.Id}{q}", H.AdminToken)).Ok().As<PagedResult<DeliveryListItem>>();

        Assert.Equal(2, (await List("")).TotalCount);
        Assert.Equal([b.Item.Id], (await List("&status=Voided")).Items.Select(i => i.Id));
        Assert.Equal(2, (await List($"&customerId={w.Customer.Id}")).TotalCount);
        Assert.Equal(0, (await List($"&from={Today(1)}")).TotalCount);
        Assert.Equal([a.Item.Id], (await List($"&q={a.Item.Code}")).Items.Select(i => i.Id));
        Assert.Equal(2, (await List($"&q={po.Code}")).TotalCount);
        Assert.Equal((po.Code, w.Customer.Name, 5), (a.Item.PoCode, a.Item.CustomerName, a.Item.TotalQty));

        ProblemAssert.Is(await H.Api.GetAsync("/api/v1/deliveries", none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.PostDeliveryAsync(po, [(s, 1)], token: viewer.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.True((await H.PostDeliveryAsync(po, [(s, 1)], token: creator.Token)).IsSuccess);
        ProblemAssert.Is(await H.VoidDeliveryAsync(a.Item.Id, token: creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.CorrectDeliveryAsync(a.Item.Id, [(s, 1)], token: creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.PutAsync($"/api/v1/deliveries/{a.Item.Id}/info", new UpdateDeliveryInfoRequest(Today(), "x", null, null, null, a.RowVersion), creator.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        Assert.Empty((await H.GetDeliveryAsync(a.Item.Id, viewer.Token)).AllowedActions);
        ProblemAssert.Is(await H.Api.GetAsync($"/api/v1/deliveries/{Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    // ===== KONKURENSI (AC-PG-06, E10) =====

    [Fact]
    public async Task Dua_surat_jalan_bersamaan_dari_alokasi_yang_sama_tepat_satu_lolos()
    {
        for (var round = 0; round < 5; round++)
        {
            var (w, po) = await H.NewDeliverableAsync(allocSmall: 10, allocMedium: 0);
            var s = po.PoLineId(w.Small);
            var gate = new TaskCompletionSource();

            Task<ApiResponse> Go() => Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostDeliveryAsync(po, [(s, 8)]);
            });

            var tasks = new[] { Go(), Go() };
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r.IsSuccess));
            var problem = ProblemAssert.Is(results.Single(r => !r.IsSuccess), HttpStatusCode.Conflict, ProblemTypes.RuleViolation);
            Assert.Equal((ProblemCodes.DeliveryExceedsAllocation, 2m), (problem.Code, problem.Violations![0].Limit!.Value)); // batas terbaru
            Assert.Equal((2, 8), await H.LineAsync(s));
            await AssertConsistentAsync(w, w.Small);
            await H.AssertInProductionInvariantAsync(po.Id);
        }
    }

    [Fact]
    public async Task Dua_puluh_surat_jalan_paralel_pada_alokasi_10_tepat_10_lolos_tanpa_5xx()
    {
        var (w, po) = await H.NewDeliverableAsync(allocSmall: 10, allocMedium: 0);
        var s = po.PoLineId(w.Small);
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            return await H.PostDeliveryAsync(po, [(s, 1)]);
        })).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, results.Count(r => r.IsSuccess));
        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        Assert.Equal((0, 10), await H.LineAsync(s));
        Assert.Equal((50, 0), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id));
        await AssertConsistentAsync(w, w.Small);
    }

    [Fact]
    public async Task Pengiriman_bersamaan_dengan_pelepasan_alokasi_tidak_pernah_merusak_invarian()
    {
        for (var round = 0; round < 5; round++)
        {
            var (w, po) = await H.NewDeliverableAsync(allocSmall: 20, allocMedium: 0);
            var s = po.PoLineId(w.Small);
            var gate = new TaskCompletionSource();

            var deliver = Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostDeliveryAsync(po, [(s, 15)]);
            });
            var release = Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync($"/api/v1/po-lines/{s}/release-allocation", new ReleaseAllocationRequest(20, null), H.AdminToken);
            });
            gate.SetResult();
            var results = await Task.WhenAll(deliver, release);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            await AssertConsistentAsync(w, w.Small);
            await H.AssertInProductionInvariantAsync(po.Id);
        }
    }

    [Fact]
    public async Task Pengiriman_bersamaan_dengan_pembatalan_PO_tidak_meninggalkan_PO_batal_yang_sudah_terkirim()
    {
        for (var round = 0; round < 4; round++)
        {
            var (w, po) = await H.NewDeliverableAsync(allocSmall: 10, allocMedium: 0);
            var s = po.PoLineId(w.Small);
            var current = await H.GetPoAsync(po.Id);
            var gate = new TaskCompletionSource();

            var deliver = Task.Run(async () =>
            {
                await gate.Task;
                return await H.PostDeliveryAsync(po, [(s, 5)]);
            });
            var cancel = Task.Run(async () =>
            {
                await gate.Task;
                return await H.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel", new ReasonRequest("batal bersamaan", current.RowVersion), H.AdminToken);
            });
            gate.SetResult();
            var results = await Task.WhenAll(deliver, cancel);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            var after = await H.GetPoAsync(po.Id);
            var (_, delivered) = await H.LineAsync(s);
            Assert.False(after.Status == PoStatus.Cancelled && delivered > 0, "PO batal tidak boleh memiliki pengiriman");
            await AssertConsistentAsync(w, w.Small);
        }
    }

    [Fact]
    public async Task Pengiriman_dua_PO_berbeda_dan_penyesuaian_pada_stok_yang_sama_tidak_pernah_kehilangan_pembaruan()
    {
        for (var round = 0; round < 4; round++)
        {
            var w = await H.NewPoWorldAsync();
            await H.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);
            var a = await H.NewPoAsync(w, [w.Line(w.Small, 50)]);
            var b = await H.NewPoAsync(w, [w.Line(w.Small, 50)]);
            (await H.Api.PostAsync($"/api/v1/po-lines/{a.Lines[0].Id}/allocate", new AllocateRequest(20, null), H.AdminToken)).Ok();
            (await H.Api.PostAsync($"/api/v1/po-lines/{b.Lines[0].Id}/allocate", new AllocateRequest(20, null), H.AdminToken)).Ok();
            var gate = new TaskCompletionSource();

            Task<ApiResponse> Run(Func<Task<ApiResponse>> call) => Task.Run(async () =>
            {
                await gate.Task;
                return await call();
            });

            var tasks = new List<Task<ApiResponse>>
            {
                Run(() => H.PostDeliveryAsync(a, [(a.Lines[0].Id, 10)])),
                Run(() => H.PostDeliveryAsync(b, [(b.Lines[0].Id, 10)])),
                Run(() => H.Api.PostAsync("/api/v1/stock/adjustments", new AdjustStockRequest(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 7, Today(), "tambah"), H.AdminToken)),
                Run(() => H.Api.PostAsync("/api/v1/stock/adjustments", new AdjustStockRequest(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, -3, Today(), "kurang"), H.AdminToken)),
            };
            gate.SetResult();
            var results = await Task.WhenAll(tasks);

            Assert.DoesNotContain(results, r => (int)r.Status >= 500);
            Assert.All(results, r => Assert.True(r.IsSuccess, r.Body)); // semuanya sah, urutan bebas
            Assert.Equal((100 - 20 + 7 - 3, 20), await H.StockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id)); // tidak ada pembaruan yang hilang
            await AssertConsistentAsync(w, w.Small);
        }
    }

    [Fact]
    public async Task Void_bersamaan_dengan_pengiriman_baru_dan_koreksi_paralel_tetap_konsisten()
    {
        var (w, po) = await H.NewDeliverableAsync(allocSmall: 30, allocMedium: 0, stockSmall: 60);
        var s = po.PoLineId(w.Small);
        var delivery = await H.DeliverAsync(po, (s, 30));
        var gate = new TaskCompletionSource();

        var correct1 = Task.Run(async () =>
        {
            await gate.Task;
            return await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 20)]);
        });
        var correct2 = Task.Run(async () =>
        {
            await gate.Task;
            return await H.CorrectDeliveryAsync(delivery.Item.Id, [(s, 10)]);
        });
        var voiding = Task.Run(async () =>
        {
            await gate.Task;
            return await H.VoidDeliveryAsync(delivery.Item.Id);
        });
        gate.SetResult();
        var results = await Task.WhenAll(correct1, correct2, voiding);

        Assert.DoesNotContain(results, r => (int)r.Status >= 500);
        Assert.Equal(1, results.Count(r => r.IsSuccess)); // dokumen asal hanya dapat diganti/di-void sekali
        await AssertConsistentAsync(w, w.Small);
        await H.AssertInProductionInvariantAsync(po.Id);
    }
}
