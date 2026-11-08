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

/// <summary>Ketersediaan stok untuk dialog alokasi (hasil hitung server, tanpa kunci).</summary>
public sealed class GetStockAvailabilityHandler(GmsDbContext db)
{
    public async Task<StockAvailabilityDto> HandleAsync(Guid poLineId, CancellationToken ct)
    {
        var line = await db.PoLines.AsNoTracking().SingleOrDefaultAsync(l => l.Id == poLineId, ct)
                   ?? throw ProblemException.NotFound("Baris PO");
        var status = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == line.PoId).Select(p => p.Status).SingleAsync(ct);

        var stock = await db.StockBalances.AsNoTracking()
            .SingleOrDefaultAsync(s => s.FgId == line.FgId && s.ColorId == line.ColorId && s.SizeId == line.SizeId, ct);
        var onHand = stock?.OnHand ?? 0;
        var allocatedAll = stock?.Allocated ?? 0;
        var available = onHand - allocatedAll;
        var remaining = line.RemainingPlanQty;
        var max = status is PoStatus.Confirmed or PoStatus.InProgress ? Math.Max(0, Math.Min(available, remaining)) : 0;

        return new StockAvailabilityDto(onHand, allocatedAll, available, remaining, line.AllocatedQty, max,
            LimitReason(status, available, remaining, max));
    }

    internal static string? LimitReason(PoStatus status, int available, int remaining, int max)
    {
        if (status is not (PoStatus.Confirmed or PoStatus.InProgress))
        {
            return "Alokasi hanya dapat dilakukan pada PO berstatus Confirmed atau In Progress.";
        }

        if (max == 0)
        {
            return remaining <= 0
                ? "Sisa Rencana baris ini sudah habis (seluruh kebutuhan sudah dialokasikan, diproduksi, atau terkirim)."
                : "Tidak ada stok tersedia untuk FG, Color, dan Size ini.";
        }

        return max < available ? $"Dibatasi oleh Sisa Rencana ({remaining})." : max < remaining ? $"Dibatasi oleh stok tersedia ({available})." : null;
    }
}

/// <summary>Riwayat alokasi seluruh baris sebuah PO (tab Alokasi), terbaru lebih dulu.</summary>
public sealed class GetPoAllocationsHandler(GmsDbContext db)
{
    public async Task<IReadOnlyList<AllocationEntryDto>> HandleAsync(Guid poId, CancellationToken ct)
    {
        if (!await db.PurchaseOrders.AnyAsync(p => p.Id == poId, ct))
        {
            throw ProblemException.NotFound("PO");
        }

        var rows = await (
            from a in db.PoAllocations.AsNoTracking()
            join l in db.PoLines on a.PoLineId equals l.Id
            where l.PoId == poId
            join f in db.Fgs on l.FgId equals f.Id
            join c in db.Colors on l.ColorId equals c.Id
            join s in db.Sizes on l.SizeId equals s.Id
            orderby a.Id descending
            select new
            {
                a.Id, a.PoLineId, FgCode = f.Code, a.Kind, a.Qty, a.Source, a.Reason, a.CreatedAt,
                ColorId = c.Id, ColorCode = c.Code, ColorName = c.Name, SizeId = s.Id, SizeCode = s.Code, SizeName = s.Name, s.SortOrder,
                CreatedByName = db.Users.Where(u => u.Id == a.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
            }).ToListAsync(ct);

        return rows.Select(r => new AllocationEntryDto(
            r.Id, r.PoLineId, r.FgCode,
            new ColorSizeRef(r.ColorId, r.ColorCode, r.ColorName, r.SizeId, r.SizeCode, r.SizeName, r.SortOrder),
            r.Kind, r.Qty, r.Source, r.Reason, r.CreatedAt, r.CreatedByName)).ToList();
    }
}

/// <summary>
/// Alokasi (reservasi) stok FG ke baris PO (FSD Customer PO 4.4). Batas = min(Stok Tersedia, Sisa Rencana). Dua pengguna yang
/// mengalokasikan stok yang sama bersamaan: yang kedua melihat ketersediaan terbaru dan ditolak bila tidak cukup (AC-PO-17).
/// Urutan kunci baku: po_lines → stock_balances. CHECK database (allocated ≤ on_hand; Sisa Rencana ≥ 0) menjadi sabuk pengaman terakhir.
/// </summary>
public sealed class AllocateStockHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, GetPurchaseOrderHandler detail)
{
    public async Task<PoLineDto> HandleAsync(Guid poLineId, AllocateRequest request, CancellationToken ct)
    {
        if (request.Qty < 1)
        {
            throw ProblemException.Validation("qty", "Qty alokasi harus lebih dari 0.");
        }

        var keys = await db.PoLines.AsNoTracking().Where(l => l.Id == poLineId)
            .Select(l => new { l.PoId, l.FgId, l.ColorId, l.SizeId }).SingleOrDefaultAsync(ct)
            ?? throw ProblemException.NotFound("Baris PO");

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await RowLocks.LockPoLinesAsync(db, [poLineId], ct);
        await RowLocks.LockStockAsync(db, keys.FgId, keys.ColorId, keys.SizeId, ct);

        var status = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == keys.PoId).Select(p => p.Status).SingleAsync(ct);
        if (status is not (PoStatus.Confirmed or PoStatus.InProgress))
        {
            throw ProblemException.StateConflict("Alokasi hanya dapat dilakukan pada PO berstatus Confirmed atau In Progress.");
        }

        var line = await db.PoLines.SingleAsync(l => l.Id == poLineId, ct);
        var stock = await db.StockBalances.SingleOrDefaultAsync(
            s => s.FgId == keys.FgId && s.ColorId == keys.ColorId && s.SizeId == keys.SizeId, ct);
        var available = (stock?.OnHand ?? 0) - (stock?.Allocated ?? 0);
        var remaining = line.RemainingPlanQty;
        var max = Math.Max(0, Math.Min(available, remaining));

        if (request.Qty > max)
        {
            var message = $"Qty alokasi {request.Qty} melebihi batas {max}: stok tersedia {available}, Sisa Rencana {remaining}.";
            throw ProblemException.Rule(ProblemCodes.AllocationExceeded, message,
                [new RuleViolation(ProblemCodes.AllocationExceeded, "Alokasi stok", request.Qty, max, message)]);
        }

        stock!.Allocated += request.Qty;
        line.AllocatedQty += request.Qty;
        line.UpdatedAt = now;
        line.UpdatedBy = user.Id;
        db.PoAllocations.Add(new PoAllocation
        {
            PoLineId = poLineId, Kind = AllocationKind.Allocate, Qty = request.Qty, Source = AllocationSource.Manual,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), CreatedAt = now, CreatedBy = user.Id,
        });
        audit.Add(AuditEvents.PoAllocated, "PoLine", poLineId.ToString(), null, new { qty = request.Qty, line.FgId, line.ColorId, line.SizeId });

        await db.SaveChangesAsync(ct);
        if (await PoLifecycle.MarkInProgressAsync(db, keys.PoId, now, ct))
        {
            audit.Add(AuditEvents.PoStatusChanged, "PurchaseOrder", keys.PoId.ToString(), new { status = "Confirmed" }, new { status = "InProgress" });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        return (await detail.HandleAsync(keys.PoId, ct)).Lines.Single(l => l.Id == poLineId);
    }
}

/// <summary>Melepas alokasi aktif sebagian atau seluruhnya (stok kembali tersedia, Sisa Rencana bertambah).</summary>
public sealed class ReleaseAllocationHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, GetPurchaseOrderHandler detail)
{
    public async Task<PoLineDto> HandleAsync(Guid poLineId, ReleaseAllocationRequest request, CancellationToken ct)
    {
        if (request.Qty < 1)
        {
            throw ProblemException.Validation("qty", "Qty yang dilepas harus lebih dari 0.");
        }

        var keys = await db.PoLines.AsNoTracking().Where(l => l.Id == poLineId)
            .Select(l => new { l.PoId, l.FgId, l.ColorId, l.SizeId }).SingleOrDefaultAsync(ct)
            ?? throw ProblemException.NotFound("Baris PO");

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await RowLocks.LockPoLinesAsync(db, [poLineId], ct);
        await RowLocks.LockStockAsync(db, keys.FgId, keys.ColorId, keys.SizeId, ct);

        var line = await db.PoLines.SingleAsync(l => l.Id == poLineId, ct);
        if (request.Qty > line.AllocatedQty)
        {
            var message = $"Qty yang dilepas {request.Qty} melebihi alokasi aktif {line.AllocatedQty}.";
            throw ProblemException.Rule(ProblemCodes.AllocationExceeded, message,
                [new RuleViolation(ProblemCodes.AllocationExceeded, "Pelepasan alokasi", request.Qty, line.AllocatedQty, message)]);
        }

        var stock = await db.StockBalances.SingleAsync(
            s => s.FgId == keys.FgId && s.ColorId == keys.ColorId && s.SizeId == keys.SizeId, ct);
        stock.Allocated -= request.Qty;
        line.AllocatedQty -= request.Qty;
        line.UpdatedAt = now;
        line.UpdatedBy = user.Id;
        db.PoAllocations.Add(new PoAllocation
        {
            PoLineId = poLineId, Kind = AllocationKind.Release, Qty = request.Qty, Source = AllocationSource.Manual,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(), CreatedAt = now, CreatedBy = user.Id,
        });
        audit.Add(AuditEvents.PoAllocationReleased, "PoLine", poLineId.ToString(), null, new { qty = request.Qty });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await detail.HandleAsync(keys.PoId, ct)).Lines.Single(l => l.Id == poLineId);
    }
}
