using Gms.Infrastructure.Persistence;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Sales;

/// <summary>
/// Perubahan status PO yang dipicu SISTEM (bukan pengguna): In Progress pada aktivitas pertama dan Completed otomatis
/// saat semua baris terkirim (FSD Customer PO 2.3). Dipakai bersama modul Work Order dan Pengiriman.
/// Memakai <c>ExecuteUpdate</c> bersyarat (atomik, tanpa token konkurensi EF) agar dua aktivitas bersamaan tidak saling menjatuhkan.
/// </summary>
public static class PoLifecycle
{
    /// <summary>Confirmed → In Progress (idempoten). Mengembalikan true bila status benar-benar berubah.</summary>
    public static async Task<bool> MarkInProgressAsync(GmsDbContext db, Guid poId, DateTimeOffset now, CancellationToken ct) =>
        await db.PurchaseOrders
            .Where(p => p.Id == poId && p.Status == PoStatus.Confirmed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PoStatus.InProgress)
                .SetProperty(p => p.UpdatedAt, (DateTimeOffset?)now), ct) > 0;

    /// <summary>
    /// Menghitung ulang kelengkapan pengiriman: semua baris terkirim penuh ⇒ Completed; PO Completed yang tidak lagi terpenuhi
    /// (mis. void pengiriman) ⇒ kembali In Progress. Mengembalikan status baru bila berubah, selain itu null.
    /// </summary>
    public static async Task<PoStatus?> RecomputeCompletionAsync(GmsDbContext db, Guid poId, DateTimeOffset now, CancellationToken ct)
    {
        var hasLines = await db.PoLines.AnyAsync(l => l.PoId == poId, ct);
        var allDelivered = hasLines && !await db.PoLines.AnyAsync(l => l.PoId == poId && l.DeliveredQty < l.Qty, ct);

        if (allDelivered)
        {
            var changed = await db.PurchaseOrders
                .Where(p => p.Id == poId && (p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PoStatus.Completed)
                    .SetProperty(p => p.UpdatedAt, (DateTimeOffset?)now), ct);
            return changed > 0 ? PoStatus.Completed : null;
        }

        var reopened = await db.PurchaseOrders
            .Where(p => p.Id == poId && p.Status == PoStatus.Completed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PoStatus.InProgress)
                .SetProperty(p => p.UpdatedAt, (DateTimeOffset?)now), ct);
        return reopened > 0 ? PoStatus.InProgress : null;
    }
}
