using Gms.Api.Common;
using Gms.Api.Features.Inventory;
using Gms.Api.Features.Sales;
using Gms.Domain.Deliveries;
using Gms.Domain.Inventory;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gms.Api.Features.Deliveries;

/// <summary>Keadaan terkunci untuk satu operasi pengiriman: seluruh baris PO (urut id), saldo stok terkait, dan label sel.</summary>
public sealed class DeliveryState
{
    public required Guid PoId { get; init; }
    public required string PoCode { get; init; }
    public required PoStatus PoStatus { get; init; }
    public required Dictionary<Guid, PoLine> Lines { get; init; }
    public required Dictionary<StockKey, StockBalance> Stocks { get; init; }
    public required Dictionary<Guid, string> Labels { get; init; }
    public required Dictionary<Guid, (int Delivered, int Allocated)> LineBefore { get; init; }
    public required Dictionary<StockKey, (int OnHand, int Allocated)> StockBefore { get; init; }

    public StockKey KeyOf(PoLine line) => new(line.FgId, line.ColorId, line.SizeId);
}

/// <summary>
/// Mesin pengiriman (FSD Pengiriman 2, 4.1, 4.3). Urutan kunci baku: po_lines (urut id) → stock_balances (fg, color, size).
/// Perubahan dibuat di memori pada entitas terkunci lalu disimpan sekali sehingga CHECK database hanya melihat kondisi akhir;
/// koreksi = <see cref="Reverse"/> + <see cref="Apply"/> dalam satu simpanan.
/// </summary>
public sealed class DeliveryEngine(GmsDbContext db, TimeProvider time, AppTimeZone zone)
{
    public DateOnly Today => zone.Today(time);

    public static bool IsDeliverable(PoStatus status) => status is PoStatus.Confirmed or PoStatus.InProgress;

    public async Task<DeliveryState> OpenAsync(Guid poId, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == poId).Select(p => new { p.Code, p.Status }).SingleOrDefaultAsync(ct)
                 ?? throw ProblemException.NotFound("PO");

        var lineIds = await db.PoLines.AsNoTracking().Where(l => l.PoId == poId).Select(l => l.Id).ToListAsync(ct);
        await RowLocks.LockPoLinesAsync(db, lineIds, ct);
        var lines = await db.PoLines.Where(l => l.PoId == poId).ToDictionaryAsync(l => l.Id, ct);

        var keys = lines.Values.Select(l => new StockKey(l.FgId, l.ColorId, l.SizeId)).Distinct().ToList();
        await RowLocks.EnsureAndLockStockAsync(db, keys.Select(k => (k.FgId, k.ColorId, k.SizeId)), ct);
        var fgIds = keys.Select(k => k.FgId).Distinct().ToList();
        var stocks = (await db.StockBalances.Where(s => fgIds.Contains(s.FgId)).ToListAsync(ct))
            .Where(s => keys.Contains(new StockKey(s.FgId, s.ColorId, s.SizeId)))
            .ToDictionary(s => new StockKey(s.FgId, s.ColorId, s.SizeId));

        var labels = await (
            from l in db.PoLines.AsNoTracking().Where(l => l.PoId == poId)
            join f in db.Fgs on l.FgId equals f.Id
            join c in db.Colors on l.ColorId equals c.Id
            join s in db.Sizes on l.SizeId equals s.Id
            select new { l.Id, Label = f.Code + " " + c.Code + "/" + s.Code }).ToDictionaryAsync(x => x.Id, x => x.Label, ct);

        return new DeliveryState
        {
            PoId = poId, PoCode = po.Code, PoStatus = po.Status, Lines = lines, Stocks = stocks, Labels = labels,
            LineBefore = lines.ToDictionary(kv => kv.Key, kv => (kv.Value.DeliveredQty, kv.Value.AllocatedQty)),
            StockBefore = stocks.ToDictionary(kv => kv.Key, kv => (kv.Value.OnHand, kv.Value.Allocated)),
        };
    }

    public static int MaxDeliver(PoLine line) => Math.Max(Math.Min(line.AllocatedQty, line.Qty - line.DeliveredQty), 0);

    /// <summary>Memeriksa batas (min(Dialokasikan, Qty − Terkirim)) lalu menerapkan pengiriman: ledger stok −, alokasi dikonsumsi, Terkirim +.</summary>
    public void Apply(DeliveryState state, Delivery doc, List<(PoLine Line, int Qty)> cells, Guid? userId, DateTimeOffset now)
    {
        var violations = new List<RuleViolation>();
        foreach (var (line, qty) in cells)
        {
            var max = MaxDeliver(line);
            if (qty > max)
            {
                var label = state.Labels[line.Id];
                var reason = line.AllocatedQty <= line.Qty - line.DeliveredQty
                    ? $"dialokasikan {line.AllocatedQty}"
                    : $"sisa kebutuhan {line.Qty - line.DeliveredQty} (Qty {line.Qty} − Terkirim {line.DeliveredQty})";
                var message = $"{label}: qty kirim {qty} melebihi batas {max} ({reason}).";
                violations.Add(new RuleViolation(ProblemCodes.DeliveryExceedsAllocation, label, qty, max, message));
            }
        }

        if (violations.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.DeliveryExceedsAllocation,
                violations.Count == 1 ? violations[0].Message : $"{violations[0].Message} (+{violations.Count - 1} pelanggaran lain).", violations);
        }

        foreach (var (line, qty) in cells)
        {
            var stock = state.Stocks[state.KeyOf(line)];
            line.AllocatedQty -= qty;
            line.DeliveredQty += qty;
            stock.OnHand -= qty;
            stock.Allocated -= qty;

            doc.Lines.Add(new DeliveryLine { DeliveryId = doc.Id, PoLineId = line.Id, Qty = qty });
            db.StockLedger.Add(new StockLedgerEntry
            {
                FgId = stock.FgId, ColorId = stock.ColorId, SizeId = stock.SizeId, Kind = StockLedgerKind.Delivery, Qty = -qty,
                SourceType = "Delivery", SourceId = doc.Id, EntryDate = doc.DeliveryDate, CreatedAt = now, CreatedBy = userId,
            });
            db.PoAllocations.Add(new PoAllocation
            {
                PoLineId = line.Id, Kind = AllocationKind.Consume, Qty = qty, Source = AllocationSource.Delivery, SourceId = doc.Id,
                Reason = $"Pengiriman {doc.Code}", CreatedAt = now, CreatedBy = userId,
            });
        }
    }

    /// <summary>Membalik pengiriman: stok +, konsumsi alokasi dibalik (menjadi Dialokasikan aktif lagi), Terkirim −.</summary>
    public void Reverse(DeliveryState state, Delivery doc, string reason, Guid? userId, DateTimeOffset now)
    {
        foreach (var dl in doc.Lines)
        {
            var line = state.Lines[dl.PoLineId];
            var stock = state.Stocks[state.KeyOf(line)];
            line.DeliveredQty -= dl.Qty;
            line.AllocatedQty += dl.Qty;
            stock.OnHand += dl.Qty;
            stock.Allocated += dl.Qty;

            db.StockLedger.Add(new StockLedgerEntry
            {
                FgId = stock.FgId, ColorId = stock.ColorId, SizeId = stock.SizeId, Kind = StockLedgerKind.DeliveryReversal, Qty = dl.Qty,
                SourceType = "Delivery", SourceId = doc.Id, EntryDate = Today, Reason = reason, CreatedAt = now, CreatedBy = userId,
            });
            db.PoAllocations.Add(new PoAllocation
            {
                PoLineId = line.Id, Kind = AllocationKind.ReverseConsume, Qty = dl.Qty, Source = AllocationSource.DeliveryVoid, SourceId = doc.Id,
                Reason = $"Pengiriman {doc.Code} dibatalkan", CreatedAt = now, CreatedBy = userId,
            });
        }

        doc.Status = DocStatus.Voided;
        doc.VoidReason = reason;
        doc.VoidedAt = now;
        doc.VoidedBy = userId;
    }

    public List<RuleViolation> CheckFinalState(DeliveryState state)
    {
        var violations = new List<RuleViolation>();
        foreach (var line in state.Lines.Values)
        {
            if (line.DeliveredQty < 0 || line.AllocatedQty < 0 || line.DeliveredQty + line.AllocatedQty + line.InProductionQty > line.Qty)
            {
                var label = state.Labels[line.Id];
                violations.Add(new RuleViolation(ProblemCodes.DeliveryExceedsAllocation, label, line.DeliveredQty, line.Qty,
                    $"{label}: Terkirim/Dialokasikan akan menjadi tidak konsisten (Terkirim {line.DeliveredQty}, Dialokasikan {line.AllocatedQty}, Qty {line.Qty})."));
            }
        }

        foreach (var (key, stock) in state.Stocks)
        {
            if (stock.OnHand < 0 || stock.Allocated < 0 || stock.Allocated > stock.OnHand)
            {
                var label = state.Lines.Values.Where(l => state.KeyOf(l) == key).Select(l => state.Labels[l.Id]).FirstOrDefault() ?? "Stok";
                violations.Add(new RuleViolation(ProblemCodes.StockInsufficient, label, stock.OnHand - stock.Allocated, 0,
                    $"{label}: stok fisik {stock.OnHand}, teralokasi {stock.Allocated} tidak konsisten."));
            }
        }

        return violations;
    }

    public static void ThrowIfAny(List<RuleViolation> violations)
    {
        if (violations.Count > 0)
        {
            throw ProblemException.Rule(violations[0].Code, violations[0].Message, violations);
        }
    }

    /// <summary>Menyimpan dengan jaring pengaman: pelanggaran CHECK database dipetakan ke 409 (bukan 500).</summary>
    public async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            throw ProblemException.Rule(ProblemCodes.DeliveryExceedsAllocation,
                "Perubahan akan membuat stok atau rencana PO tidak konsisten. Muat ulang dan coba lagi.");
        }
    }

    /// <summary>Menerapkan status PO yang diturunkan dari pengiriman aktif; mengembalikan status baru bila berubah.</summary>
    public async Task<PoStatus?> RefreshPoStatusAsync(Guid poId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await PoLifecycle.MarkInProgressAsync(db, poId, now, ct);
        return await PoLifecycle.RecomputeCompletionAsync(db, poId, now, ct);
    }

    public static ImpactPreview BuildImpact(DeliveryState state, List<RuleViolation> violations, IEnumerable<string> notes)
    {
        var rows = new List<ImpactRow>();
        foreach (var (id, line) in state.Lines)
        {
            var before = state.LineBefore[id];
            if (before == (line.DeliveredQty, line.AllocatedQty))
            {
                continue;
            }

            var label = state.Labels[id];
            rows.Add(new ImpactRow(label, "Terkirim", before.Delivered, line.DeliveredQty, line.Qty, line.DeliveredQty is >= 0 && line.DeliveredQty <= line.Qty, null));
            rows.Add(new ImpactRow(label, "Dialokasikan", before.Allocated, line.AllocatedQty, null, line.AllocatedQty >= 0,
                violations.FirstOrDefault(v => v.Subject == label)?.Message));
        }

        foreach (var (key, stock) in state.Stocks)
        {
            var before = state.StockBefore[key];
            if (before != (stock.OnHand, stock.Allocated))
            {
                var label = state.Lines.Values.Where(l => state.KeyOf(l) == key).Select(l => state.Labels[l.Id]).FirstOrDefault() ?? "Stok";
                rows.Add(new ImpactRow(label, "Stok Fisik", before.OnHand, stock.OnHand, null, stock.OnHand >= 0, null));
            }
        }

        return new ImpactPreview(rows, violations.Count == 0, violations, notes.ToList());
    }
}
