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

    /// <summary>
    /// Menghitung ulang <c>in_production_qty</c> (Dalam Produksi) baris PO dari baris WO aktif (ADR-004):
    /// Σ max(qty WO − diserahterimakan, 0) untuk WO Draft/Released/In Progress/Completed. Dihitung ulang (bukan delta) agar tidak ada drift.
    /// WAJIB dipanggil dalam transaksi, di bawah kunci po_lines, SETELAH perubahan WO disimpan.
    /// </summary>
    public static async Task RecomputeInProductionAsync(GmsDbContext db, IReadOnlyCollection<Guid> poLineIds, CancellationToken ct)
    {
        if (poLineIds.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE po_lines pl SET in_production_qty = COALESCE((
                SELECT SUM(GREATEST(wl.qty - wl.handed_over_qty, 0))
                FROM wo_lines wl JOIN work_orders w ON w.id = wl.wo_id
                WHERE wl.po_line_id = pl.id AND w.status IN ('Draft','Released','InProgress','Completed')), 0)
            WHERE pl.id = ANY({0})
            """,
            [poLineIds.ToArray()], ct);
    }
}
