using Gms.Api.Common;
using Gms.Api.Features.Production;
using Gms.Domain.Inventory;
using Gms.Domain.Production;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gms.Api.Features.Inventory;

public sealed record StockKey(Guid FgId, Guid ColorId, Guid SizeId);

/// <summary>Keadaan terkunci untuk satu operasi serah terima: WO, baris PO, saldo stok, dan output langkah terakhir.</summary>
public sealed class HandoverState
{
    public required WoContext Ctx { get; init; }
    public required bool PoActive { get; init; }
    public required string PoCode { get; init; }
    public required Dictionary<Guid, PoLine> PoLines { get; init; }
    public required Dictionary<StockKey, StockBalance> Stocks { get; init; }
    public required Dictionary<Guid, int> LastOutput { get; init; }
    public required Dictionary<StockKey, (int OnHand, int Allocated)> StockBefore { get; init; }

    public StockKey KeyOf(WoLine line)
    {
        var pl = PoLines[line.PoLineId];
        return new StockKey(pl.FgId, pl.ColorId, pl.SizeId);
    }
}

public sealed record CellEstimate(Guid WoLineId, int Auto, int Free);

/// <summary>
/// Mesin serah terima FG (FSD Serah Terima 2.2/2.3): batas qty, alokasi otomatis ke baris PO asal, ledger stok, dan pembalikan.
/// Urutan kunci baku: WO → po_lines (urut id) → stock_balances (urut fg, color, size). Semua perubahan dilakukan di memori pada entitas
/// terkunci lalu disimpan sekali, sehingga CHECK database hanya melihat KONDISI AKHIR (koreksi = pembalikan + dokumen baru).
/// </summary>
public sealed class HandoverEngine(GmsDbContext db, StepLedger ledger)
{
    public DateOnly Today => ledger.Today;

    public static void RequireHandoverStatus(WorkOrder wo)
    {
        if (wo.Status is WoStatus.InProgress or WoStatus.Completed)
        {
            return;
        }

        var message = wo.Status switch
        {
            WoStatus.Closed => "WO sudah ditutup. Minta Comfi melakukan Reopen terlebih dahulu.",
            WoStatus.Released => "Belum ada output pada WO ini; serah terima FG belum dapat dicatat.",
            WoStatus.Draft => "WO masih Draft.",
            _ => "WO telah dibatalkan.",
        };
        throw ProblemException.StateConflict(message, ProblemCodes.WoStatusInvalid);
    }

    public async Task<HandoverState> OpenAsync(Guid woId, CancellationToken ct)
    {
        var ctx = await ledger.BeginAsync(woId, [], ct);
        RequireHandoverStatus(ctx.Wo);

        var poLineIds = ctx.Lines.Select(l => l.PoLineId).Distinct().ToList();
        await RowLocks.LockPoLinesAsync(db, poLineIds, ct);
        var poLines = await db.PoLines.Where(p => poLineIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var keys = poLines.Values.Select(p => new StockKey(p.FgId, p.ColorId, p.SizeId)).Distinct().ToList();
        await RowLocks.EnsureAndLockStockAsync(db, keys.Select(k => (k.FgId, k.ColorId, k.SizeId)), ct);
        var fgIds = keys.Select(k => k.FgId).Distinct().ToList();
        var rows = await db.StockBalances.Where(s => fgIds.Contains(s.FgId)).ToListAsync(ct);
        var stocks = rows.Where(s => keys.Contains(new StockKey(s.FgId, s.ColorId, s.SizeId)))
            .ToDictionary(s => new StockKey(s.FgId, s.ColorId, s.SizeId));

        var lineIds = ctx.Lines.Select(l => l.Id).ToList();
        var lastSeq = ctx.Steps.Count == 0 ? (int?)null : ctx.Steps[^1].Seq;
        var last = lastSeq is { } seq
            ? await db.WoStepBalances.AsNoTracking().Where(b => lineIds.Contains(b.WoLineId) && b.Seq == seq).ToDictionaryAsync(b => b.WoLineId, b => b.Qty, ct)
            : [];
        var lastOutput = ctx.Lines.ToDictionary(l => l.Id, l => last.GetValueOrDefault(l.Id));

        var po = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == ctx.Wo.PoId).Select(p => new { p.Code, p.Status }).SingleAsync(ct);
        return new HandoverState
        {
            Ctx = ctx, PoActive = po.Status is PoStatus.Confirmed or PoStatus.InProgress, PoCode = po.Code,
            PoLines = poLines, Stocks = stocks, LastOutput = lastOutput,
            StockBefore = stocks.ToDictionary(kv => kv.Key, kv => (kv.Value.OnHand, kv.Value.Allocated)),
        };
    }

    public static int MaxHandover(HandoverState state, WoLine line) => Math.Max(state.LastOutput[line.Id] - line.HandedOverQty, 0);

    /// <summary>Menetapkan qty diserahterimakan baris WO sambil menjaga <c>in_production_qty</c> baris PO (Σ max(qty − diserahterimakan, 0)).</summary>
    private static void SetHanded(HandoverState state, WoLine line, int newHanded)
    {
        var oldRemaining = Math.Max(line.Qty - line.HandedOverQty, 0);
        var newRemaining = Math.Max(line.Qty - newHanded, 0);
        state.PoLines[line.PoLineId].InProductionQty += newRemaining - oldRemaining;
        line.HandedOverQty = newHanded;
    }

    /// <summary>Memeriksa batas lalu menerapkan dokumen baru. Mengembalikan perkiraan alokasi per sel.</summary>
    public List<CellEstimate> Apply(
        HandoverState state, FgHandover doc, List<(WoLine Line, int Qty)> cells, Guid? userId, DateTimeOffset now)
    {
        var violations = new List<RuleViolation>();
        foreach (var (line, qty) in cells)
        {
            var max = MaxHandover(state, line);
            if (qty > max)
            {
                var label = state.Ctx.Label(line);
                var message = $"{label}: serah terima {qty} melebihi batas {max} (output langkah terakhir {state.LastOutput[line.Id]} − sudah diserahterimakan {line.HandedOverQty}).";
                violations.Add(new RuleViolation(ProblemCodes.HandoverExceedsOutput, label, qty, max, message));
            }
        }

        if (violations.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.HandoverExceedsOutput, violations[0].Message, violations);
        }

        var estimates = new List<CellEstimate>();
        foreach (var (line, qty) in cells)
        {
            var poLine = state.PoLines[line.PoLineId];
            var stock = state.Stocks[state.KeyOf(line)];
            var auto = state.PoActive ? Math.Min(qty, Math.Max(line.Qty - line.HandedOverQty, 0)) : 0;

            SetHanded(state, line, line.HandedOverQty + qty);
            poLine.AllocatedQty += auto;
            stock.OnHand += qty;
            stock.Allocated += auto;

            doc.Lines.Add(new FgHandoverLine { HandoverId = doc.Id, WoLineId = line.Id, Qty = qty, AutoAllocatedQty = auto });
            db.StockLedger.Add(new StockLedgerEntry
            {
                FgId = stock.FgId, ColorId = stock.ColorId, SizeId = stock.SizeId, Kind = StockLedgerKind.HandoverReceipt, Qty = qty,
                SourceType = "FgHandover", SourceId = doc.Id, EntryDate = doc.HandoverDate, CreatedAt = now, CreatedBy = userId,
            });
            if (auto > 0)
            {
                db.PoAllocations.Add(new PoAllocation
                {
                    PoLineId = poLine.Id, Kind = AllocationKind.Allocate, Qty = auto, Source = AllocationSource.Handover, SourceId = doc.Id,
                    Reason = "Alokasi otomatis dari serah terima FG", CreatedAt = now, CreatedBy = userId,
                });
            }

            estimates.Add(new CellEstimate(line.Id, auto, qty - auto));
        }

        return estimates;
    }

    /// <summary>Membalik dokumen: melepas alokasi otomatis yang masih aktif, mengurangi stok, menandai Voided.</summary>
    public void Reverse(HandoverState state, FgHandover doc, string reason, Guid? userId, DateTimeOffset now)
    {
        foreach (var dl in doc.Lines)
        {
            var line = state.Ctx.Line(dl.WoLineId)!;
            var poLine = state.PoLines[line.PoLineId];
            var stock = state.Stocks[state.KeyOf(line)];
            var release = Math.Min(dl.AutoAllocatedQty, poLine.AllocatedQty);

            SetHanded(state, line, line.HandedOverQty - dl.Qty);
            poLine.AllocatedQty -= release;
            stock.Allocated -= release;
            stock.OnHand -= dl.Qty;

            db.StockLedger.Add(new StockLedgerEntry
            {
                FgId = stock.FgId, ColorId = stock.ColorId, SizeId = stock.SizeId, Kind = StockLedgerKind.HandoverReversal, Qty = -dl.Qty,
                SourceType = "FgHandover", SourceId = doc.Id, EntryDate = Today, Reason = reason, CreatedAt = now, CreatedBy = userId,
            });
            if (release > 0)
            {
                db.PoAllocations.Add(new PoAllocation
                {
                    PoLineId = poLine.Id, Kind = AllocationKind.Release, Qty = release, Source = AllocationSource.HandoverVoid, SourceId = doc.Id,
                    Reason = "Serah terima FG di-void", CreatedAt = now, CreatedBy = userId,
                });
            }
        }

        doc.Status = DocStatus.Voided;
        doc.VoidReason = reason;
        doc.VoidedAt = now;
        doc.VoidedBy = userId;
    }

    /// <summary>Pemeriksaan KONDISI AKHIR stok dan baris PO setelah semua perubahan di memori.</summary>
    public List<RuleViolation> CheckFinalState(HandoverState state)
    {
        var violations = new List<RuleViolation>();
        foreach (var (key, stock) in state.Stocks)
        {
            if (stock.OnHand >= 0 && stock.Allocated >= 0 && stock.Allocated <= stock.OnHand)
            {
                continue;
            }

            var label = StockLabel(state, key);
            var shipped = state.PoLines.Values.Any(p => p.FgId == key.FgId && p.ColorId == key.ColorId && p.SizeId == key.SizeId && p.DeliveredQty > 0);
            var message = stock.OnHand < 0 || shipped && stock.OnHand < state.StockBefore[key].OnHand
                ? $"{label}: stok fisik akan menjadi {stock.OnHand}. Stok sudah dikirim ke customer; batalkan pengiriman terkait terlebih dahulu."
                : $"{label}: stok tersedia akan menjadi {stock.OnHand - stock.Allocated}. Stok sudah dialokasikan ke PO lain; lepas alokasi terlebih dahulu.";
            violations.Add(new RuleViolation(ProblemCodes.StockInsufficient, label, stock.OnHand - stock.Allocated, 0, message));
        }

        foreach (var poLine in state.PoLines.Values)
        {
            if (poLine.DeliveredQty + poLine.AllocatedQty + poLine.InProductionQty > poLine.Qty || poLine.InProductionQty < 0 || poLine.AllocatedQty < 0)
            {
                var label = $"Baris PO {state.PoCode}";
                violations.Add(new RuleViolation(ProblemCodes.StockInsufficient, label, null, poLine.Qty,
                    $"{label}: Terkirim + Dialokasikan + Dalam Produksi melebihi Qty PO ({poLine.Qty}). Stok sudah dikirim; batalkan pengiriman terkait terlebih dahulu."));
            }
        }

        return violations;
    }

    private static string StockLabel(HandoverState state, StockKey key)
    {
        var line = state.Ctx.Lines.FirstOrDefault(l => state.PoLines[l.PoLineId] is { } p && p.ColorId == key.ColorId && p.SizeId == key.SizeId);
        return line is null ? "Stok" : $"Stok {state.Ctx.Label(line)}";
    }

    public static void ThrowIfAny(List<RuleViolation> violations)
    {
        if (violations.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.StockInsufficient,
                violations.Count == 1 ? violations[0].Message : $"{violations[0].Message} (+{violations.Count - 1} pelanggaran lain).", violations);
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
            throw ProblemException.Rule(ProblemCodes.StockInsufficient,
                "Perubahan akan membuat stok atau rencana PO tidak konsisten (stok sudah dialokasikan atau dikirim).");
        }
    }

    /// <summary>Tabel dampak stok sebelum → sesudah untuk void/koreksi/penyesuaian.</summary>
    public static ImpactPreview BuildImpact(HandoverState state, List<RuleViolation> violations, IEnumerable<string> notes)
    {
        var rows = new List<ImpactRow>();
        foreach (var (key, stock) in state.Stocks)
        {
            var before = state.StockBefore[key];
            if (before == (stock.OnHand, stock.Allocated))
            {
                continue;
            }

            var label = StockLabel(state, key);
            rows.Add(new ImpactRow(label, "Stok Fisik", before.OnHand, stock.OnHand, null, stock.OnHand >= 0, null));
            rows.Add(new ImpactRow(label, "Teralokasi", before.Allocated, stock.Allocated, stock.OnHand, stock.Allocated <= stock.OnHand, null));
            var available = stock.OnHand - stock.Allocated;
            rows.Add(new ImpactRow(label, "Stok Tersedia", before.OnHand - before.Allocated, available, 0, available >= 0,
                available >= 0 ? null : violations.FirstOrDefault(v => v.Subject == label)?.Message));
        }

        return new ImpactPreview(rows, violations.Count == 0, violations, notes.ToList());
    }
}
