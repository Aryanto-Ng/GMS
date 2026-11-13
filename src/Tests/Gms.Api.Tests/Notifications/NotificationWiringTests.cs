using System.Net;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Notifications;
using Gms.Shared.Security;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Notifications;

/// <summary>
/// Notifikasi Kejadian dari alur bisnis nyata (FSD NE-01..NE-12): pelaku tidak menerima, pemegang izin menerima,
/// parameter benar, dan aksi yang ditolak tidak meninggalkan notifikasi (atomik, AC-NT-03).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationWiringTests(ApiFixture fixture)
{
    private static readonly string[] ObserverPermissions =
    [
        Permissions.ProductionOutput.View, Permissions.ProductionOutput.Create, Permissions.WorkOrder.View, Permissions.WorkOrder.Close,
        Permissions.StokFG.View, Permissions.SerahTerimaFG.View, Permissions.Pengiriman.View, Permissions.CustomerPO.View, Permissions.Payroll.View,
        Permissions.VendorJob.View,
    ];

    private static async Task<TestUser> ObserverAsync(TestHost h) =>
        await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, ObserverPermissions)).Id);

    private static async Task<List<NotificationItem>> ListAsync(TestHost h, string token) =>
        (await h.Api.GetAsync("/api/v1/notifications?pageSize=200", token)).Ok().As<PagedResult<NotificationItem>>().Items.ToList();

    private static async Task<NotificationItem> SingleAsync(TestHost h, string token, string typeCode) =>
        (await ListAsync(h, token)).Single(n => n.TypeCode == typeCode);

    [Fact]
    public async Task WO_dirilis_dan_selesai_memberi_tahu_pemegang_izin_tetapi_tidak_pelaku()
    {
        var h = await fixture.CreateIsolatedAsync();
        var observer = await ObserverAsync(h);
        var (_, _, wo) = await h.NewReleasedWoAsync();

        var released = await SingleAsync(h, observer.Token, "NE-01");
        Assert.Equal(wo.Code, released.Params["code"]);
        Assert.False(string.IsNullOrEmpty(released.Params["fg"]));
        Assert.Equal(NotificationTargetKind.WorkOrder, released.Target!.Kind);
        Assert.Equal(wo.Id.ToString(), released.Target.Id);
        Assert.DoesNotContain(await ListAsync(h, h.AdminToken), n => n.TypeCode == "NE-01"); // pelaku = Administrator

        await h.SetWoStatusAsync(wo.Id, WoStatus.InProgress);
        await h.WoDoAsync(wo.Id, "complete");
        Assert.Equal(wo.Code, (await SingleAsync(h, observer.Token, "NE-02")).Params["code"]);
    }

    [Fact]
    public async Task Void_output_dan_serah_terima_FG_memberi_tahu_dan_void_serah_terima_melepas_alokasi_NE12()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewProdSetupAsync();
        var observer = await ObserverAsync(h);

        var worker = await h.NewWorkerAsync();
        var entry = await h.OutAsync(s, s.Cut, worker.Id, (s.LineS, 10));
        Assert.Equal(HttpStatusCode.NoContent, (await h.VoidAsync(entry)).Status);
        var voided = await SingleAsync(h, observer.Token, "NE-05");
        Assert.Equal(("void", "10"), (voided.Params["action"], voided.Params["qty"]));
        Assert.Equal(s.Wo.Code, voided.Params["wo"]);

        await h.ProduceAsync(s, 20);
        var handover = await h.HandoverAsync(s, (s.LineS, 20));
        var created = await SingleAsync(h, observer.Token, "NE-03");
        Assert.Equal("20", created.Params["qty"]);
        Assert.Equal(handover.AutoAllocatedQty.ToString(), created.Params["allocatedQty"]);
        Assert.Equal(NotificationTargetKind.FgHandover, created.Target!.Kind);

        (await h.VoidHandoverAsync(handover.Id)).Ok();
        var handoverVoid = await SingleAsync(h, observer.Token, "NE-06");
        Assert.Equal("void", handoverVoid.Params["action"]);
        var released = await SingleAsync(h, observer.Token, "NE-12");
        Assert.Equal(handover.AutoAllocatedQty.ToString(), released.Params["qty"]);
        Assert.Equal(NotificationTargetKind.PurchaseOrder, released.Target!.Kind);
    }

    [Fact]
    public async Task Pengiriman_void_dan_stok_habis_serta_PO_dibatalkan_diberitahukan_dan_penolakan_tidak_meninggalkan_notifikasi()
    {
        var h = await fixture.CreateIsolatedAsync();
        // Stok S = 50, dialokasikan 40 -> tersedia 10.
        var (w, po) = await h.NewDeliverableAsync(poSmall: 100, poMedium: 50, allocSmall: 40, allocMedium: 20, stockSmall: 50, stockMedium: 30);
        var observer = await ObserverAsync(h);

        // Mengalokasikan sisa 10 membuat Tersedia turun dari > 0 ke 0 -> NE-11.
        (await h.Api.PostAsync($"/api/v1/po-lines/{po.PoLineId(w.Small)}/allocate", new AllocateRequest(10, null), h.AdminToken)).Ok();
        var depleted = await SingleAsync(h, observer.Token, "NE-11");
        Assert.Equal(NotificationTargetKind.StockLedger, depleted.Target!.Kind);
        Assert.False(string.IsNullOrEmpty(depleted.Params["fg"]));

        // Alokasi berikutnya tidak melewati transisi (tetap 0): tidak ada NE-11 baru.
        var delivery = await h.DeliverAsync(po, (po.PoLineId(w.Small), 10));
        Assert.Single(await ListAsync(h, observer.Token), n => n.TypeCode == "NE-11");

        // Pembatalan PO ditolak (sudah ada pengiriman): aksi gagal, tidak ada NE-08.
        var current = await h.GetPoAsync(po.Id);
        ProblemAssertShim.Conflict(await h.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel", new ReasonRequest("batal", current.RowVersion), h.AdminToken));
        Assert.DoesNotContain(await ListAsync(h, observer.Token), n => n.TypeCode == "NE-08");

        (await h.VoidDeliveryAsync(delivery.Item.Id)).Ok();
        var voided = await SingleAsync(h, observer.Token, "NE-07");
        Assert.Equal(("void", delivery.Item.Code), (voided.Params["action"], voided.Params["code"]));
    }

    [Fact]
    public async Task PO_dibatalkan_memberi_tahu_dengan_jumlah_alokasi_yang_dilepas()
    {
        var h = await fixture.CreateIsolatedAsync();
        var (_, po) = await h.NewDeliverableAsync();
        var observer = await ObserverAsync(h);
        var current = await h.GetPoAsync(po.Id);

        (await h.Api.PostAsync($"/api/v1/purchase-orders/{po.Id}/cancel", new ReasonRequest("batal", current.RowVersion), h.AdminToken)).Ok();
        var item = await SingleAsync(h, observer.Token, "NE-08");
        Assert.Equal(po.Code, item.Params["code"]);
        Assert.Equal("60", item.Params["releasedQty"]);
        Assert.DoesNotContain(await ListAsync(h, observer.Token), n => n.TypeCode == "NE-12"); // tidak ganda dengan NE-08
    }

    [Fact]
    public async Task Periode_gaji_ditutup_memberi_tahu_pemegang_izin_Payroll()
    {
        var h = await fixture.CreateIsolatedAsync();
        var s = await h.NewRatedSetupAsync();
        var worker = await h.NewWorkerAsync();
        await h.CutAsync(s, worker.Id, Today(-3), (s.LineS, 10));
        var observer = await ObserverAsync(h);

        var period = await h.NewPeriodAsync(Today(-3), Today(-1));
        await h.ClosePeriodAsync(period.Id);
        Assert.Equal(period.Code, (await SingleAsync(h, observer.Token, "NE-04")).Params["code"]);
    }

    [Fact]
    public async Task Akun_terkunci_dan_sesi_diakhiri_paksa_diberitahukan_ke_Administrator()
    {
        var h = await fixture.CreateIsolatedAsync();
        var target = await h.CreateReadyUserAsync((await h.CreateRoleAsync(null, Permissions.CustomerPO.View)).Id);
        var admin2 = await h.CreateReadyAdminAsync();
        var observer = await h.CreateReadyAdminAsync();

        for (var i = 0; i < 6; i++)
        {
            await h.Api.PostAsync("/api/v1/auth/login", new LoginRequest(target.UserName, "salah-" + i));
        }

        var locked = await SingleAsync(h, h.AdminToken, "NE-09");
        Assert.Equal(target.UserName, locked.Params["username"]);
        Assert.Equal(NotificationTargetKind.UserList, locked.Target!.Kind);
        Assert.Single(await ListAsync(h, h.AdminToken), n => n.TypeCode == "NE-09"); // percobaan ke-6 tidak menggandakan

        // Pengguna biasa tidak melihat notifikasi keamanan.
        Assert.DoesNotContain(await ListAsync(h, target.Token), n => n.Category == NotificationCategory.Keamanan);

        (await h.Api.PostAsync($"/api/v1/users/{admin2.Id}/end-session", null, h.AdminToken)).Ok();
        var ended = await SingleAsync(h, observer.Token, "NE-10");
        Assert.Equal(admin2.UserName, ended.Params["username"]);
    }
}

internal static class ProblemAssertShim
{
    public static void Conflict(ApiResponse response) => Assert.Equal(HttpStatusCode.Conflict, response.Status);
}
