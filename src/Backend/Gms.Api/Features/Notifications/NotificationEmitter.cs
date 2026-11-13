using System.Globalization;
using Gms.Api.Security;
using Gms.Domain.Deliveries;
using Gms.Domain.Inventory;
using Gms.Domain.Notifications;
using Gms.Domain.Payroll;
using Gms.Domain.Production;
using Gms.Domain.Sales;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Features.Notifications;

/// <summary>
/// Fasad untuk handler sumber: merakit parameter notifikasi Kejadian (NE-01..NE-10) dan menerbitkannya lewat
/// <see cref="INotificationPublisher"/>. Dipanggil SEBELUM <c>SaveChanges</c> pada transaksi aksi (atomik, ADR-016).
/// NE-11 (stok habis) dan NE-12 (alokasi dilepas) dipicu <see cref="NotificationSideEffectsInterceptor"/> karena berasal dari
/// banyak jalur penulisan stok/alokasi. Parameter hanya memuat label, tanggal, dan jumlah (tanpa tarif atau rupiah).
/// </summary>
public sealed class NotificationEmitter(
    INotificationPublisher publisher,
    GmsDbContext db,
    ICurrentUser user,
    TimeProvider time,
    IOptions<AuthOptions> auth)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private string Stamp => time.GetUtcNow().UtcTicks.ToString(Inv);

    private Task PublishAsync(string type, string key, NotificationTargetKind kind, string targetId, Guid? actor, IDictionary<string, string> p, CancellationToken ct) =>
        publisher.PublishAsync(new NotificationEvent(type, key, new Dictionary<string, string>(p), new NotificationTarget(kind, targetId), actor), ct);

    private async Task<string> WoCodeAsync(Guid woId, CancellationToken ct) =>
        await db.WorkOrders.AsNoTracking().Where(w => w.Id == woId).Select(w => w.Code).FirstOrDefaultAsync(ct) ?? string.Empty;

    private async Task<string> ProcessNameAsync(Guid woId, int seq, CancellationToken ct) =>
        await db.WoRoutingSnapshots.AsNoTracking().Where(r => r.WoId == woId && r.Seq == seq).Select(r => r.ProcessName).FirstOrDefaultAsync(ct) ?? string.Empty;

    /// <summary>NE-01 WO dirilis (setelah label FG snapshot terisi).</summary>
    public async Task WoReleasedAsync(WorkOrder wo, CancellationToken ct)
    {
        var customer = await (
            from p in db.PurchaseOrders.AsNoTracking()
            where p.Id == wo.PoId
            join c in db.Customers.AsNoTracking() on p.CustomerId equals c.Id
            select c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        await PublishAsync("NE-01", $"NE-01:{wo.Id}:released", NotificationTargetKind.WorkOrder, wo.Id.ToString(), user.Id,
            new Dictionary<string, string> { ["code"] = wo.Code, ["fg"] = wo.FgCode, ["customer"] = customer }, ct);
    }

    /// <summary>NE-02 WO Completed menunggu Close (WO dapat Completed lebih dari sekali bila dibuka kembali).</summary>
    public Task WoCompletedAsync(WorkOrder wo, CancellationToken ct) =>
        PublishAsync("NE-02", $"NE-02:{wo.Id}:{Stamp}", NotificationTargetKind.WorkOrder, wo.Id.ToString(), user.Id,
            new Dictionary<string, string> { ["code"] = wo.Code }, ct);

    /// <summary>NE-03 Serah Terima FG dibuat.</summary>
    public async Task FgHandoverCreatedAsync(FgHandover doc, int autoAllocated, int freeStock, CancellationToken ct) =>
        await PublishAsync("NE-03", $"NE-03:{doc.Id}", NotificationTargetKind.FgHandover, doc.Id.ToString(), user.Id,
            new Dictionary<string, string>
            {
                ["wo"] = await WoCodeAsync(doc.WoId, ct),
                ["qty"] = (autoAllocated + freeStock).ToString(Inv),
                ["allocatedQty"] = autoAllocated.ToString(Inv),
                ["freeQty"] = freeStock.ToString(Inv),
            }, ct);

    /// <summary>NE-04 Periode gaji ditutup.</summary>
    public Task PayrollClosedAsync(PayrollPeriod period, CancellationToken ct) =>
        PublishAsync("NE-04", $"NE-04:{period.Id}", NotificationTargetKind.PayrollPeriod, period.Id.ToString(), user.Id,
            new Dictionary<string, string> { ["code"] = period.Code }, ct);

    /// <summary>NE-05 Void/Koreksi entri output. <paramref name="action"/> = <c>void</c> atau <c>correct</c>.</summary>
    public async Task OutputChangedAsync(OutputEntry entry, string action, CancellationToken ct)
    {
        var worker = await db.Workers.AsNoTracking().Where(w => w.Id == entry.WorkerId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var qty = await db.OutputEntryLines.AsNoTracking().Where(l => l.EntryId == entry.Id).SumAsync(l => (int?)l.Qty, ct) ?? 0;
        await PublishAsync("NE-05", $"NE-05:{entry.Id}:{action}", NotificationTargetKind.OutputEntry, entry.Id.ToString(), user.Id,
            new Dictionary<string, string>
            {
                ["action"] = action,
                ["wo"] = await WoCodeAsync(entry.WoId, ct),
                ["process"] = await ProcessNameAsync(entry.WoId, entry.Seq, ct),
                ["worker"] = worker,
                ["workDate"] = entry.WorkDate.ToString("yyyy-MM-dd", Inv),
                ["qty"] = qty.ToString(Inv),
            }, ct);
    }

    /// <summary>NE-05 (bagian vendor) Void/Koreksi kirim/terima vendor; nama vendor menempati parameter <c>worker</c>.</summary>
    public async Task VendorChangedAsync(VendorMovement movement, string action, CancellationToken ct)
    {
        var vendor = await db.Vendors.AsNoTracking().Where(v => v.Id == movement.VendorId).Select(v => v.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var qty = await db.VendorMovementLines.AsNoTracking().Where(l => l.MovementId == movement.Id).SumAsync(l => (int?)l.Qty, ct) ?? 0;
        await PublishAsync("NE-05", $"NE-05:{movement.Id}:{action}", NotificationTargetKind.OutputEntry, movement.Id.ToString(), user.Id,
            new Dictionary<string, string>
            {
                ["action"] = action,
                ["wo"] = await WoCodeAsync(movement.WoId, ct),
                ["process"] = await ProcessNameAsync(movement.WoId, movement.Seq, ct),
                ["worker"] = vendor,
                ["workDate"] = movement.MovementDate.ToString("yyyy-MM-dd", Inv),
                ["qty"] = qty.ToString(Inv),
            }, ct);
    }

    /// <summary>NE-06 Void/Koreksi Serah Terima FG.</summary>
    public async Task FgHandoverChangedAsync(FgHandover doc, string action, CancellationToken ct)
    {
        var qty = await db.FgHandoverLines.AsNoTracking().Where(l => l.HandoverId == doc.Id).SumAsync(l => (int?)l.Qty, ct) ?? 0;
        await PublishAsync("NE-06", $"NE-06:{doc.Id}:{action}", NotificationTargetKind.FgHandover, doc.Id.ToString(), user.Id,
            new Dictionary<string, string> { ["action"] = action, ["wo"] = await WoCodeAsync(doc.WoId, ct), ["qty"] = qty.ToString(Inv) }, ct);
    }

    /// <summary>NE-07 Void/Koreksi Pengiriman.</summary>
    public async Task DeliveryChangedAsync(Delivery delivery, string action, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == delivery.PoId).Select(p => p.Code).FirstOrDefaultAsync(ct) ?? string.Empty;
        await PublishAsync("NE-07", $"NE-07:{delivery.Id}:{action}", NotificationTargetKind.Delivery, delivery.Id.ToString(), user.Id,
            new Dictionary<string, string> { ["action"] = action, ["code"] = delivery.Code, ["po"] = po }, ct);
    }

    /// <summary>NE-08 PO dibatalkan (menyebut jumlah alokasi yang dilepas; NE-12 tidak dikirim untuk pembatalan PO agar tidak ganda).</summary>
    public Task PoCancelledAsync(PurchaseOrder po, int releasedQty, CancellationToken ct) =>
        PublishAsync("NE-08", $"NE-08:{po.Id}", NotificationTargetKind.PurchaseOrder, po.Id.ToString(), user.Id,
            new Dictionary<string, string> { ["code"] = po.Code, ["releasedQty"] = releasedQty.ToString(Inv) }, ct);

    /// <summary>NE-09 Akun terkunci. Pelaku = sistem (bukan pengguna yang terkunci), sehingga Administrator tetap menerima.</summary>
    public Task AccountLockedAsync(AppUser locked, CancellationToken ct) =>
        PublishAsync("NE-09", $"NE-09:{locked.Id}:{locked.LockoutEnd?.UtcTicks ?? 0}", NotificationTargetKind.UserList, locked.Id.ToString(), null,
            new Dictionary<string, string> { ["username"] = locked.UserName ?? string.Empty, ["attempts"] = auth.Value.MaxFailedAttempts.ToString(Inv) }, ct);

    /// <summary>NE-10 Sesi pengguna diakhiri paksa oleh Administrator.</summary>
    public async Task SessionEndedAsync(Guid userId, CancellationToken ct)
    {
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct) ?? string.Empty;
        await PublishAsync("NE-10", $"NE-10:{userId}:{Stamp}", NotificationTargetKind.UserList, userId.ToString(), user.Id,
            new Dictionary<string, string>
            {
                ["username"] = name,
                ["at"] = time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv),
            }, ct);
    }
}
