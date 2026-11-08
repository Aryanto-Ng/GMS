using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Sales;

/// <summary>Konfirmasi PO (Draft → Confirmed). Nomor PO customer boleh masih kosong; PO wajib memiliki minimal 1 baris (AC-PO-08/09).</summary>
public sealed class ConfirmPurchaseOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, PoQueries queries)
{
    public async Task<PoDto> HandleAsync(Guid id, uint rowVersion, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ProblemException.NotFound("PO");
        db.Entry(po).Property(p => p.RowVersion).OriginalValue = rowVersion;

        if (po.Status != PoStatus.Draft)
        {
            throw ProblemException.StateConflict($"Hanya PO berstatus Draft yang dapat dikonfirmasi (status saat ini: {po.Status}).");
        }

        if (!await db.PoLines.AnyAsync(l => l.PoId == id, ct))
        {
            throw ProblemException.Validation("lines", "PO tidak dapat dikonfirmasi tanpa baris.");
        }

        var now = time.GetUtcNow();
        po.Status = PoStatus.Confirmed;
        po.ConfirmedAt = now;
        po.ConfirmedBy = user.Id;
        po.UpdatedAt = now;
        po.UpdatedBy = user.Id;
        audit.Add(AuditEvents.PoConfirmed, "PurchaseOrder", id.ToString(), new { status = "Draft" }, new { status = "Confirmed" });
        await db.SaveChangesAsync(ct);

        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>Mengisi/mengubah Nomor PO Customer kapan pun sampai PO selesai (AC-PO-03).</summary>
public sealed class SetCustomerPoNumberHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, PoQueries queries)
{
    public async Task<PoDto> HandleAsync(Guid id, SetCustomerPoNumberRequest request, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ProblemException.NotFound("PO");
        db.Entry(po).Property(p => p.RowVersion).OriginalValue = request.RowVersion;

        if (po.Status is PoStatus.Completed or PoStatus.Cancelled)
        {
            throw ProblemException.StateConflict("Nomor PO customer tidak dapat diubah pada PO yang sudah selesai atau dibatalkan.");
        }

        var number = PoValidation.NormalizeCustomerPoNumber(request.CustomerPoNumber);
        if (number is { Length: > 100 })
        {
            throw ProblemException.Validation("customerPoNumber", "Nomor PO customer maksimal 100 karakter.");
        }

        await PoValidation.EnsureCustomerPoNumberUniqueAsync(db, po.CustomerId, number, id, ct);

        var old = po.CustomerPoNumber;
        po.CustomerPoNumber = number;
        po.UpdatedAt = time.GetUtcNow();
        po.UpdatedBy = user.Id;
        audit.Add(AuditEvents.PoCustomerNumberChanged, "PurchaseOrder", id.ToString(), new { customerPoNumber = old }, new { customerPoNumber = number });
        await db.SaveChangesAsync(ct);

        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>
/// Membatalkan PO dengan alasan wajib. Ditolak bila sudah ada pengiriman atau WO aktif (AC-PO-26). Seluruh alokasi aktif dilepas
/// otomatis dan stok kembali tersedia (AC-PO-27), dalam satu transaksi dengan urutan kunci baku (po_lines → stock_balances).
/// </summary>
public sealed class CancelPurchaseOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, PoQueries queries)
{
    public async Task<PoDto> HandleAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 500)
        {
            throw ProblemException.Validation("reason", "Alasan pembatalan wajib diisi (maksimal 500 karakter).");
        }

        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ProblemException.NotFound("PO");
        db.Entry(po).Property(p => p.RowVersion).OriginalValue = request.RowVersion;

        if (po.Status is PoStatus.Cancelled or PoStatus.Completed)
        {
            throw ProblemException.StateConflict($"PO berstatus {po.Status} tidak dapat dibatalkan.");
        }

        var blockers = await queries.CancelBlockersAsync(id, ct);
        if (blockers.Count > 0)
        {
            throw ProblemException.Rule("po.cancel-blocked",
                "PO tidak dapat dibatalkan karena sudah ada pengiriman atau WO aktif. Batalkan/tutup terlebih dahulu, atau turunkan Qty melalui revisi.",
                blockers.Select(b => new RuleViolation("po.cancel-blocked", b, null, null, b)).ToList());
        }

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var lineIds = await db.PoLines.AsNoTracking().Where(l => l.PoId == id).Select(l => l.Id).ToListAsync(ct);
        await RowLocks.LockPoLinesAsync(db, lineIds, ct);
        var lines = await db.PoLines.Where(l => l.PoId == id).ToListAsync(ct);

        var allocated = lines.Where(l => l.AllocatedQty > 0).ToList();
        await RowLocks.LockStockAsync(db, allocated.Select(l => (l.FgId, l.ColorId, l.SizeId)), ct);

        var released = 0;
        foreach (var line in allocated)
        {
            var stock = await db.StockBalances.SingleAsync(s => s.FgId == line.FgId && s.ColorId == line.ColorId && s.SizeId == line.SizeId, ct);
            stock.Allocated -= line.AllocatedQty;
            released += line.AllocatedQty;
            db.PoAllocations.Add(new PoAllocation
            {
                PoLineId = line.Id, Kind = AllocationKind.Release, Qty = line.AllocatedQty,
                Source = AllocationSource.PoCancel, Reason = "PO dibatalkan", CreatedAt = now, CreatedBy = user.Id,
            });
            line.AllocatedQty = 0;
        }

        var previous = po.Status;
        po.Status = PoStatus.Cancelled;
        po.CancelReason = reason;
        po.UpdatedAt = now;
        po.UpdatedBy = user.Id;
        audit.Add(AuditEvents.PoCancelled, "PurchaseOrder", id.ToString(),
            new { status = previous.ToString() }, new { status = "Cancelled", reason, releasedAllocation = released });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await queries.GetDetailAsync(id, ct);
    }
}
