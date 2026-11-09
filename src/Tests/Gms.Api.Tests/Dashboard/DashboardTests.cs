using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Dashboard;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Dashboard;

/// <summary>Beranda: widget read model per izin (FSD Beranda). Angka agregat diuji pada host terisolasi.</summary>
[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFixture fixture)
{
    private static async Task<T> Widget<T>(TestHost h, string id, string? token = null) =>
        (await h.Api.GetAsync($"/api/v1/dashboard/{id}", token ?? h.AdminToken)).Ok(id).As<T>();

    // ===== Izin per widget =====

    [Fact]
    public async Task Setiap_widget_hanya_dapat_dibaca_dengan_izin_sumbernya_dan_tidak_ada_data_untuk_yang_tidak_berhak()
    {
        var h = fixture.Main;
        var map = new Dictionary<string, string>
        {
            [DashboardWidgetIds.PoLate] = Permissions.CustomerPO.View, [DashboardWidgetIds.DueSoon] = Permissions.CustomerPO.View,
            [DashboardWidgetIds.PoNoCustomerNumber] = Permissions.CustomerPO.View, [DashboardWidgetIds.UnplannedDemand] = Permissions.CustomerPO.View,
            [DashboardWidgetIds.WoRunning] = Permissions.WorkOrder.View, [DashboardWidgetIds.WoLate] = Permissions.WorkOrder.View,
            [DashboardWidgetIds.OutputToday] = Permissions.ProductionOutput.View, [DashboardWidgetIds.VendorBalance] = Permissions.VendorJob.View,
            [DashboardWidgetIds.UnhandedOver] = Permissions.SerahTerimaFG.View, [DashboardWidgetIds.StockSummary] = Permissions.StokFG.View,
            [DashboardWidgetIds.PayrollPeriod] = Permissions.Payroll.View, [DashboardWidgetIds.Security] = Permissions.AccessManagement.AuditLog.View,
        };
        var nobody = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.Pengiriman.View)).Id);

        foreach (var (widget, permission) in map)
        {
            // Izin audit log hanya boleh dimiliki Administrator (tidak dapat diberikan ke role lain), jadi widget keamanan diuji dengan admin.
            var token = permission == Permissions.AccessManagement.AuditLog.View
                ? h.AdminToken
                : (await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, permission)).Id)).Token;
            Assert.True((await h.Api.GetAsync($"/api/v1/dashboard/{widget}", token)).IsSuccess, widget);
            ProblemAssert.Is(await h.Api.GetAsync($"/api/v1/dashboard/{widget}", nobody.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        }

        // Pemegang satu izin tidak dapat membaca widget lain (E6).
        var poOnly = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        ProblemAssert.Is(await h.Api.GetAsync($"/api/v1/dashboard/{DashboardWidgetIds.StockSummary}", poOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await h.Api.GetAsync($"/api/v1/dashboard/{DashboardWidgetIds.PayrollPeriod}", poOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await h.Api.GetAsync("/api/v1/dashboard/widget-tidak-ada", h.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Api.GetAsync($"/api/v1/dashboard/{DashboardWidgetIds.PoLate}", null)).Status);
    }

    // ===== Penjualan =====

    [Fact]
    public async Task Widget_PO_menghitung_terlambat_jatuh_tempo_nomor_customer_kosong_dan_kebutuhan_belum_terencana()
    {
        var h = await fixture.CreateIsolatedAsync();
        var w = await h.NewPoWorldAsync();
        await h.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 100);

        // PO A (Confirmed, tanpa nomor customer): baris terlambat 10 hari (qty 50, terkirim 20), baris +3 hari (30), baris +30 hari (40).
        var a = await h.NewPoAsync(w, [w.Line(w.Small, 50, -10), w.Line(w.Medium, 30, 3), w.Line(w.Small, 40, 30)]);
        var lateLine = a.Lines.Single(l => l.DeliveryDate == Today(-10)).Id;
        await h.UpdateLineAsync(lateLine, l => l.DeliveredQty = 20);
        // PO B (Confirmed, nomor ada): terlambat 2 hari, qty 15.
        var b = await h.NewPoAsync(w, [w.Line(w.Medium, 15, -2)], customerPoNumber: "NO-B");
        // PO C (Draft, tanpa nomor): tidak dihitung sebagai terlambat/tenggat, tetapi dihitung "tanpa nomor".
        await h.NewPoAsync(w, [w.Line(w.Small, 5, -20)], confirm: false);
        // PO D (Cancelled): tidak dihitung di mana pun.
        var d = await h.NewPoAsync(w, [w.Line(w.Small, 7, -30)]);
        (await h.Api.PostAsync($"/api/v1/purchase-orders/{d.Id}/cancel", new ReasonRequest("batal", d.RowVersion), h.AdminToken)).Ok();

        var late = await Widget<PoLateWidget>(h, DashboardWidgetIds.PoLate);
        Assert.Equal((2, 30 + 15), (late.PoCount, late.ShortageQty)); // A: 50−20 ; B: 15
        Assert.Equal([a.Id, b.Id], late.Items.Select(i => i.PoId)); // paling lama terlambat dulu
        Assert.Equal((10, 30, w.Customer.Name), (late.Items[0].DaysLate, late.Items[0].ShortageQty, late.Items[0].CustomerName));
        Assert.Equal(2, late.Items[1].DaysLate);

        var due = await Widget<DueSoonWidget>(h, DashboardWidgetIds.DueSoon);
        Assert.Equal((1, 30), (due.PoCount, due.Qty)); // hanya baris +3 hari pada PO A; +30 hari di luar jendela

        var noNumber = await Widget<PoNoCustomerNumberWidget>(h, DashboardWidgetIds.PoNoCustomerNumber);
        Assert.Equal(2, noNumber.PoCount); // A (Confirmed) + C (Draft); B punya nomor; D dibatalkan

        var unplanned = await Widget<UnplannedDemandWidget>(h, DashboardWidgetIds.UnplannedDemand);
        // Sisa Rencana: A = (50−20) + 30 + 40 = 100 ; B = 15 ; Draft/Cancelled tidak dihitung.
        Assert.Equal((2, 115), (unplanned.PoCount, unplanned.Qty));
    }

    [Fact]
    public async Task Sisa_Rencana_mengurangi_alokasi_dan_Dalam_Produksi_dan_jendela_7_hari_inklusif_dengan_zona_aplikasi()
    {
        var h = await fixture.CreateIsolatedAsync();
        var w = await h.NewPoWorldAsync();
        await h.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, 50);
        var po = await h.NewPoAsync(w, [w.Line(w.Small, 100, 0), w.Line(w.Medium, 10, 7), w.Line(w.Medium, 11, 8)]);
        (await h.Api.PostAsync($"/api/v1/po-lines/{po.Lines.First(l => l.ColorSize.SizeId == w.Small.Id).Id}/allocate", new AllocateRequest(40, null), h.AdminToken)).Ok();
        await h.NewWoAsync(w, po, (po.Lines.First(l => l.ColorSize.SizeId == w.Small.Id).Id, 30));

        var unplanned = await Widget<UnplannedDemandWidget>(h, DashboardWidgetIds.UnplannedDemand);
        Assert.Equal((1, (100 - 40 - 30) + 10 + 11), (unplanned.PoCount, unplanned.Qty));

        var due = await Widget<DueSoonWidget>(h, DashboardWidgetIds.DueSoon);
        Assert.Equal((1, 100 + 10), (due.PoCount, due.Qty)); // hari ini dan hari ke-7 masuk; hari ke-8 tidak
        Assert.Equal(0, (await Widget<PoLateWidget>(h, DashboardWidgetIds.PoLate)).PoCount); // jatuh tempo HARI INI belum terlambat
    }

    // ===== Produksi =====

    [Fact]
    public async Task Widget_WO_menghitung_per_status_urutan_target_terdekat_progres_dan_terlambat()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewProdSetupAsync(poQty: 300, woQtySmall: 100, woQtyMedium: 0 + 100);
        var w = s.World;
        var po = s.Po;
        // WO kedua dan ketiga dari PO yang sama (sisa rencana cukup).
        var second = await h.NewWoAsync(w, po, (po.PoLineId(w.Small), 50));
        var third = await h.NewWoAsync(w, po, (po.PoLineId(w.Medium), 40));
        await h.Api.PutAsync($"/api/v1/work-orders/{s.Wo.Id}/schedule", new UpdateWoScheduleRequest(null, Today(10), null, (await h.GetWoAsync(s.Wo.Id)).RowVersion), h.AdminToken);
        second = await h.WoDoAsync(second.Id, "release");
        await h.Api.PutAsync($"/api/v1/work-orders/{second.Id}/schedule", new UpdateWoScheduleRequest(null, Today(-2), null, (await h.GetWoAsync(second.Id)).RowVersion), h.AdminToken); // terlambat
        // Output 50 pada WO pertama sampai langkah terakhir → progres 25% (50/200).
        var worker = await h.NewWorkerAsync();
        var vendor = await h.NewVendorAsync();
        await h.OutAsync(s, s.Cut, worker.Id, (s.LineS, 50));
        await h.SendAsync(s, vendor.Id, (s.LineS, 50));
        await h.ReceiveAsync(s, vendor.Id, (s.LineS, 50));
        await h.OutAsync(s, s.Sew, worker.Id, (s.LineS, 50));
        await h.SetWoStatusAsync(third.Id, WoStatus.Completed);

        var running = await Widget<WoRunningWidget>(h, DashboardWidgetIds.WoRunning);

        Assert.Equal((1, 1, 1), (running.Released, running.InProgress, running.CompletedAwaitingClose));
        Assert.Equal([second.Id, s.Wo.Id], running.Items.Select(i => i.Id)); // target terdekat dulu
        Assert.Equal(25m, running.Items.Single(i => i.Id == s.Wo.Id).ProgressPercent);
        Assert.Equal(0m, running.Items.Single(i => i.Id == second.Id).ProgressPercent);
        Assert.Equal(1, (await Widget<WoLateWidget>(h, DashboardWidgetIds.WoLate)).Count);
    }

    [Fact]
    public async Task Output_hari_ini_dipecah_per_proses_tanpa_entri_void_atau_hari_lain()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync(1000m);
        var worker = await h.NewWorkerAsync();
        await h.CutAsync(s, worker.Id, Today(0), (s.LineS, 30), (s.LineM, 10));
        var voided = await h.CutAsync(s, worker.Id, Today(0), (s.LineS, 99));
        await h.VoidAsync(voided);
        await h.CutAsync(s, worker.Id, Today(-1), (s.LineS, 500)); // kemarin

        var widget = await Widget<OutputTodayWidget>(h, DashboardWidgetIds.OutputToday);

        Assert.Equal(40, widget.TotalQty);
        var cutting = Assert.Single(widget.ByProcess);
        Assert.Equal(40, cutting.Qty);
        Assert.Equal(s.World.Fg.Cut.Name, cutting.ProcessName);
    }

    [Fact]
    public async Task Sisa_di_vendor_per_vendor_tanpa_WO_Closed()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync(1000m);
        var worker = await h.NewWorkerAsync();
        var a = await h.NewVendorAsync("CV Alfa");
        var b = await h.NewVendorAsync("CV Beta");
        await h.CutAsync(s, worker.Id, Today(0), (s.LineS, 100));
        await h.SendAsync(s, a.Id, (s.LineS, 60));
        await h.SendAsync(s, b.Id, (s.LineS, 30));
        await h.ReceiveAsync(s, a.Id, (s.LineS, 25));

        var widget = await Widget<VendorBalanceWidget>(h, DashboardWidgetIds.VendorBalance);

        Assert.Equal(35 + 30, widget.TotalQty);
        Assert.Equal([("CV Alfa", 35), ("CV Beta", 30)], widget.ByVendor.Select(v => (v.VendorName, v.Qty)));

        await h.SetWoStatusAsync(s.Wo.Id, WoStatus.Closed);
        Assert.Equal(0, (await Widget<VendorBalanceWidget>(h, DashboardWidgetIds.VendorBalance)).TotalQty);
    }

    // ===== Gudang =====

    [Fact]
    public async Task Belum_diserahterimakan_dan_ringkasan_stok_FG()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewProdSetupAsync(poQty: 200, woQtySmall: 100, woQtyMedium: 100);
        await h.ProduceAsync(s, 60, 20);
        await h.HandoverAsync(s, (s.LineS, 25));

        var unhanded = await Widget<UnhandedOverWidget>(h, DashboardWidgetIds.UnhandedOver);
        Assert.Equal(60 + 20 - 25, unhanded.TotalQty);
        var item = Assert.Single(unhanded.Items);
        Assert.Equal((s.Wo.Id, s.Wo.Code, 55), (item.WoId, item.WoCode, item.Qty));

        var stock = await Widget<StockSummaryWidget>(h, DashboardWidgetIds.StockSummary);
        Assert.Equal((25, 25, 0), (stock.OnHand, stock.Allocated, stock.Available)); // seluruh serah terima dialokasikan otomatis ke PO
        Assert.Empty(stock.TopFgs); // tidak ada stok bebas

        await h.Api.PostAsync("/api/v1/stock/adjustments", new Gms.Shared.Inventory.AdjustStockRequest(s.World.Fg.Fg.Id, s.World.Color.Id, s.World.Medium.Id, 12, Today(), "temuan"), h.AdminToken);
        var after = await Widget<StockSummaryWidget>(h, DashboardWidgetIds.StockSummary);
        Assert.Equal((37, 25, 12), (after.OnHand, after.Allocated, after.Available));
        var top = Assert.Single(after.TopFgs);
        Assert.Equal((s.World.Fg.Fg.Code, 12), (top.FgCode, top.Available));

        await h.SetWoStatusAsync(s.Wo.Id, WoStatus.Closed);
        Assert.Equal(0, (await Widget<UnhandedOverWidget>(h, DashboardWidgetIds.UnhandedOver)).TotalQty); // WO Closed tidak dihitung
    }

    // ===== Penggajian dan keamanan =====

    [Fact]
    public async Task Widget_periode_gaji_saran_awal_total_sementara_sisa_hari_dan_saran_periode_berikutnya()
    {
        var h = await fixture.CreateIsolatedAsync();
        var empty = await Widget<PayrollPeriodWidget>(h, DashboardWidgetIds.PayrollPeriod);
        Assert.Equal((false, null), (empty.HasOpenPeriod, empty.SuggestedNextStart));

        var s = await h.NewRatedSetupAsync(1000m);
        var worker = await h.NewWorkerAsync();
        await h.CutAsync(s, worker.Id, Today(-3), (s.LineS, 10));
        Assert.Equal(Today(-3), (await Widget<PayrollPeriodWidget>(h, DashboardWidgetIds.PayrollPeriod)).SuggestedNextStart); // output tertua

        var period = (await h.CreatePeriodAsync(Today(-4), Today(3))).Ok().As<PayrollPeriodDto>();
        var open = await Widget<PayrollPeriodWidget>(h, DashboardWidgetIds.PayrollPeriod);
        Assert.Equal((true, period.Id, period.Code, 1, 10000m, 3), (open.HasOpenPeriod, open.PeriodId, open.Code, open.WorkerCount, open.ProvisionalTotal, open.DaysLeft));
        Assert.Null(open.SuggestedNextStart);

        await h.ClosePeriodAsync(period.Id);
        var closed = await Widget<PayrollPeriodWidget>(h, DashboardWidgetIds.PayrollPeriod);
        Assert.Equal((false, Today(4)), (closed.HasOpenPeriod, closed.SuggestedNextStart));
    }

    [Fact]
    public async Task Widget_keamanan_menghitung_akun_terkunci_dan_login_gagal_24_jam()
    {
        var h = await fixture.CreateIsolatedAsync();
        var role = await h.CreateRoleAsync(null, Permissions.CustomerPO.View);
        var victim = await h.CreateUserAsync(role.Id);
        var before = await Widget<SecurityWidget>(h, DashboardWidgetIds.Security);

        for (var i = 0; i < 2; i++)
        {
            await h.Api.PostAsync("/api/v1/auth/login", new LoginRequest(victim.UserName, "Salah12345"));
        }

        await using (var db = h.CreateDbContext())
        {
            await db.Users.Where(u => u.Id == victim.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.LockoutEnd, DateTimeOffset.UtcNow.AddMinutes(10)));
            // Login gagal lama (> 24 jam) tidak dihitung.
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_logs DISABLE TRIGGER USER");
            await db.Database.ExecuteSqlRawAsync("UPDATE audit_logs SET occurred_at = now() - interval '30 hours' WHERE event_type = 'LoginFailed' AND id = (SELECT min(id) FROM audit_logs WHERE event_type = 'LoginFailed')");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_logs ENABLE TRIGGER USER");
        }

        var after = await Widget<SecurityWidget>(h, DashboardWidgetIds.Security);

        Assert.Equal(before.LockedAccounts + 1, after.LockedAccounts);
        Assert.Equal(before.FailedLogins24h + 1, after.FailedLogins24h); // dua percobaan, satu di antaranya digeser ke 30 jam lalu
    }
}
