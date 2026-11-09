using Gms.Api.Common;
using Gms.Api.Features.Sales;
using Gms.Domain.Production;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Production;

public sealed record StepInfo(int Seq, ProcessType Type, string Code, string Name);

public sealed record Balance(int Qty, int Sent);

/// <summary>WO yang sudah terkunci beserta routing snapshot dan barisnya. Hanya valid di dalam transaksi pemanggil.</summary>
public sealed class WoContext(WorkOrder wo, List<StepInfo> steps, List<WoLine> lines)
{
    public WorkOrder Wo { get; } = wo;
    public List<StepInfo> Steps { get; } = steps;
    public List<WoLine> Lines { get; } = lines;

    public StepInfo? Step(int seq) => Steps.FirstOrDefault(s => s.Seq == seq);

    public WoLine? Line(Guid woLineId) => Lines.FirstOrDefault(l => l.Id == woLineId);

    public string Label(WoLine line) => $"{line.ColorCode}/{line.SizeCode}";
}

/// <summary>Perubahan saldo per (baris WO, langkah): sebelum dan sesudah penghitungan ulang dari ledger.</summary>
public sealed record LedgerDiff(
    Dictionary<(Guid LineId, int Seq), Balance> Before,
    Dictionary<(Guid LineId, int Seq), Balance> After);

/// <summary>
/// Mesin invarian kumulatif Production Output (FSD 2.2, ADR-004). Pola: kunci periode gaji (share) → kunci WO → ubah ledger →
/// hitung ulang saldo dari ledger aktif → validasi rantai pada KONDISI AKHIR → commit (atau rollback bila melanggar / pratinjau).
/// Satu kunci per WO membuat dua penyimpanan bersamaan terserialisasi (AC-PR-35).
/// </summary>
public sealed class StepLedger(GmsDbContext db, TimeProvider time, AppTimeZone zone)
{
    public DateOnly Today => zone.Today(time);

    // ===== Awal transaksi =====

    /// <summary>Mengunci periode gaji terkait (share) lalu WO, memuat WO + routing snapshot + baris. Harus di dalam transaksi.</summary>
    public async Task<WoContext> BeginAsync(Guid woId, IEnumerable<DateOnly> periodDates, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == woId, ct))
        {
            throw ProblemException.NotFound("WO");
        }

        await RowLocks.LockPayrollPeriodsSharedAsync(db, periodDates, ct);
        await RowLocks.LockWorkOrderAsync(db, woId, ct);

        var wo = await db.WorkOrders.SingleAsync(w => w.Id == woId, ct);
        var steps = await db.WoRoutingSnapshots.AsNoTracking().Where(s => s.WoId == woId).OrderBy(s => s.Seq)
            .Select(s => new StepInfo(s.Seq, s.ProcessType, s.ProcessCode, s.ProcessName)).ToListAsync(ct);
        var lines = await db.WoLines.Where(l => l.WoId == woId).OrderBy(l => l.ColorCode).ThenBy(l => l.SizeCode).ToListAsync(ct);
        return new WoContext(wo, steps, lines);
    }

    /// <summary>Pencatatan baru/void/koreksi hanya pada WO Released atau In Progress (FSD 2.3).</summary>
    public static void RequireOpenForEntries(WorkOrder wo)
    {
        if (wo.Status is WoStatus.Released or WoStatus.InProgress)
        {
            return;
        }

        var message = wo.Status switch
        {
            WoStatus.Completed => "WO telah ditandai selesai. Kembalikan WO ke In Progress terlebih dahulu.",
            WoStatus.Closed => "WO sudah ditutup. Minta Comfi melakukan Reopen terlebih dahulu.",
            WoStatus.Draft => "WO masih Draft. Rilis WO terlebih dahulu.",
            _ => "WO telah dibatalkan.",
        };
        throw ProblemException.StateConflict(message, ProblemCodes.WoStatusInvalid);
    }

    public static string DisabledReasonFor(WoStatus status) => status switch
    {
        WoStatus.Completed => "WO telah ditandai selesai (kembalikan ke In Progress).",
        WoStatus.Closed => "WO sudah ditutup (Reopen diperlukan).",
        WoStatus.Draft => "WO masih Draft.",
        WoStatus.Cancelled => "WO dibatalkan.",
        _ => string.Empty,
    };

    // ===== Periode gaji =====

    public async Task<bool> IsInClosedPeriodAsync(DateOnly date, CancellationToken ct) =>
        await db.PayrollPeriods.AsNoTracking().AnyAsync(p => p.Status == PayrollStatus.Closed && p.StartDate <= date && p.EndDate >= date, ct);

    /// <summary>Tanggal kerja input baru: tidak di masa depan, tidak di periode tertutup, tidak sebelum periode pertama (AC-PR-09/10/10a).</summary>
    public async Task EnsureWorkDateAllowedAsync(DateOnly date, string field, CancellationToken ct)
    {
        if (date > Today)
        {
            throw ProblemException.Validation(field, "Tanggal tidak boleh di masa depan.");
        }

        if (await IsInClosedPeriodAsync(date, ct))
        {
            throw ProblemException.PeriodClosed("Periode gaji sudah ditutup, gunakan Penyesuaian.");
        }

        var first = await db.PayrollPeriods.AsNoTracking().MinAsync(p => (DateOnly?)p.StartDate, ct);
        if (first is { } start && date < start)
        {
            throw ProblemException.Rule(ProblemCodes.PayrollDateBeforeFirstPeriod,
                $"Tanggal kerja {date:dd-MM-yyyy} lebih awal dari periode gaji pertama ({start:dd-MM-yyyy}).");
        }
    }

    // ===== Hitung ulang saldo dari ledger =====

    /// <summary>Menghitung ulang <c>wo_step_balances</c> seluruh baris WO dari entri AKTIF. Wajib setelah ledger disimpan.</summary>
    public async Task<LedgerDiff> RecomputeAsync(WoContext ctx, CancellationToken ct)
    {
        var woId = ctx.Wo.Id;
        var lineIds = ctx.Lines.Select(l => l.Id).ToList();

        var output = await (
            from l in db.OutputEntryLines.AsNoTracking()
            join e in db.OutputEntries.AsNoTracking() on l.EntryId equals e.Id
            where e.WoId == woId && e.Status == DocStatus.Active
            group l by new { l.WoLineId, e.Seq } into g
            select new { g.Key.WoLineId, g.Key.Seq, Qty = g.Sum(x => x.Qty) }).ToListAsync(ct);

        var vendor = await (
            from l in db.VendorMovementLines.AsNoTracking()
            join m in db.VendorMovements.AsNoTracking() on l.MovementId equals m.Id
            where m.WoId == woId && m.Status == DocStatus.Active
            group l by new { l.WoLineId, m.Seq, m.Kind } into g
            select new { g.Key.WoLineId, g.Key.Seq, g.Key.Kind, Qty = g.Sum(x => x.Qty) }).ToListAsync(ct);

        var existing = await db.WoStepBalances.Where(b => lineIds.Contains(b.WoLineId)).ToDictionaryAsync(b => (b.WoLineId, b.Seq), ct);
        var before = existing.ToDictionary(kv => kv.Key, kv => new Balance(kv.Value.Qty, kv.Value.SentQty));
        var after = new Dictionary<(Guid, int), Balance>();

        foreach (var line in ctx.Lines)
        {
            foreach (var step in ctx.Steps)
            {
                int qty;
                var sent = 0;
                if (step.Type == ProcessType.Internal)
                {
                    qty = output.Where(o => o.WoLineId == line.Id && o.Seq == step.Seq).Sum(o => o.Qty);
                }
                else
                {
                    qty = vendor.Where(v => v.WoLineId == line.Id && v.Seq == step.Seq && v.Kind == VendorMovementKind.Receive).Sum(v => v.Qty);
                    sent = vendor.Where(v => v.WoLineId == line.Id && v.Seq == step.Seq && v.Kind == VendorMovementKind.Send).Sum(v => v.Qty);
                }

                after[(line.Id, step.Seq)] = new Balance(qty, sent);
                if (existing.TryGetValue((line.Id, step.Seq), out var row))
                {
                    row.Qty = qty;
                    row.SentQty = sent;
                }
                else if (qty != 0 || sent != 0)
                {
                    db.WoStepBalances.Add(new WoStepBalance { WoLineId = line.Id, Seq = step.Seq, Qty = qty, SentQty = sent });
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return new LedgerDiff(before, after);
    }

    // ===== Validasi rantai pada kondisi akhir =====

    /// <summary>
    /// Memeriksa rantai <c>Output(n−1) ≥ Terkirim(n) ≥ Diterima(n) = Output(n) ≥ Kum(n+1)</c> untuk setiap sel, ditambah
    /// Diterima ≤ Terkirim per vendor. <paramref name="touched"/> = langkah yang diubah operasi ini (menentukan kode galat).
    /// </summary>
    public async Task<List<RuleViolation>> ValidateAsync(
        WoContext ctx, IReadOnlyDictionary<(Guid LineId, int Seq), Balance> balances, IReadOnlySet<int> touched, CancellationToken ct)
    {
        var violations = new List<RuleViolation>();
        Balance Of(Guid lineId, int seq) => balances.GetValueOrDefault((lineId, seq)) ?? new Balance(0, 0);

        foreach (var line in ctx.Lines)
        {
            var label = ctx.Label(line);
            int? previousOutput = null;
            foreach (var step in ctx.Steps)
            {
                var b = Of(line.Id, step.Seq);
                var subject = $"{label} · langkah {step.Seq} ({step.Name})";

                if (b.Qty < 0 || b.Sent < 0)
                {
                    violations.Add(new RuleViolation(ProblemCodes.OutputCumulativeExceeded, subject, Math.Min(b.Qty, b.Sent), 0,
                        $"{subject}: kumulatif tidak boleh negatif."));
                }

                if (step.Type == ProcessType.Internal)
                {
                    if (previousOutput is { } bound && b.Qty > bound)
                    {
                        var code = touched.Contains(step.Seq) ? ProblemCodes.OutputCumulativeExceeded : ProblemCodes.OutputBelowDownstream;
                        var message = touched.Contains(step.Seq)
                            ? $"{subject}: kumulatif {b.Qty} melebihi output langkah sebelumnya ({bound}); kelebihan {b.Qty - bound}."
                            : $"{subject}: sudah {b.Qty}, sementara output langkah sebelumnya akan menjadi {bound}.";
                        violations.Add(new RuleViolation(code, subject, b.Qty, bound, message));
                    }

                    previousOutput = b.Qty;
                }
                else
                {
                    var sentBound = previousOutput ?? line.Qty;
                    if (b.Sent > sentBound)
                    {
                        var code = touched.Contains(step.Seq) ? ProblemCodes.VendorSentExceeded : ProblemCodes.OutputBelowDownstream;
                        var message = touched.Contains(step.Seq)
                            ? $"{subject}: terkirim {b.Sent} melebihi {(previousOutput is null ? "Qty WO" : "output langkah sebelumnya")} ({sentBound})."
                            : $"{subject}: sudah terkirim {b.Sent}, sementara output langkah sebelumnya akan menjadi {sentBound}.";
                        violations.Add(new RuleViolation(code, subject, b.Sent, sentBound, message));
                    }

                    if (b.Qty > b.Sent)
                    {
                        violations.Add(new RuleViolation(ProblemCodes.VendorReceivedExceeded, subject, b.Qty, b.Sent,
                            $"{subject}: diterima {b.Qty} melebihi terkirim ({b.Sent})."));
                    }

                    previousOutput = b.Qty;
                }
            }
        }

        await AddPerVendorViolationsAsync(ctx, violations, ct);
        return violations;
    }

    private async Task AddPerVendorViolationsAsync(WoContext ctx, List<RuleViolation> violations, CancellationToken ct)
    {
        if (ctx.Steps.All(s => s.Type != ProcessType.Vendor))
        {
            return;
        }

        var rows = await (
            from l in db.VendorMovementLines.AsNoTracking()
            join m in db.VendorMovements.AsNoTracking() on l.MovementId equals m.Id
            where m.WoId == ctx.Wo.Id && m.Status == DocStatus.Active
            group l by new { l.WoLineId, m.Seq, m.VendorId, m.Kind } into g
            select new { g.Key.WoLineId, g.Key.Seq, g.Key.VendorId, g.Key.Kind, Qty = g.Sum(x => x.Qty) }).ToListAsync(ct);

        foreach (var group in rows.GroupBy(r => (r.WoLineId, r.Seq, r.VendorId)))
        {
            var sent = group.Where(r => r.Kind == VendorMovementKind.Send).Sum(r => r.Qty);
            var received = group.Where(r => r.Kind == VendorMovementKind.Receive).Sum(r => r.Qty);
            if (received <= sent)
            {
                continue;
            }

            var line = ctx.Line(group.Key.WoLineId)!;
            var vendorName = await db.Vendors.AsNoTracking().Where(v => v.Id == group.Key.VendorId).Select(v => v.Name).SingleAsync(ct);
            var subject = $"{ctx.Label(line)} · langkah {group.Key.Seq} · {vendorName}";
            violations.Add(new RuleViolation(ProblemCodes.VendorReceivedExceeded, subject, received, sent,
                $"{subject}: diterima {received} melebihi terkirim ke vendor ini ({sent})."));
        }
    }

    public static void ThrowIfAny(List<RuleViolation> violations)
    {
        if (violations.Count == 0)
        {
            return;
        }

        var first = violations[0];
        throw ProblemException.Rule(first.Code,
            violations.Count == 1 ? first.Message : $"{first.Message} (+{violations.Count - 1} pelanggaran lain).", violations);
    }

    // ===== Status WO =====

    /// <summary>Released → In Progress otomatis pada output/terima pertama (AC-PR-07/25) + status PO Confirmed → In Progress.</summary>
    public async Task MarkInProgressAsync(WoContext ctx, Guid? userId, CancellationToken ct)
    {
        if (ctx.Wo.Status != WoStatus.Released)
        {
            return;
        }

        var now = time.GetUtcNow();
        db.WoStatusHistory.Add(new WoStatusHistory
        {
            WoId = ctx.Wo.Id, FromStatus = WoStatus.Released, ToStatus = WoStatus.InProgress, Reason = "Output pertama dicatat",
            ChangedAt = now, ChangedBy = userId,
        });
        ctx.Wo.Status = WoStatus.InProgress;
        ctx.Wo.UpdatedAt = now;
        ctx.Wo.UpdatedBy = userId;
        await PoLifecycle.MarkInProgressAsync(db, ctx.Wo.PoId, now, ct);
    }

    // ===== Pratinjau dampak =====

    /// <summary>Baris dampak untuk sel yang berubah: kumulatif per langkah sebelum → sesudah terhadap batas (output langkah sebelumnya).</summary>
    public static ImpactPreview BuildPreview(WoContext ctx, LedgerDiff diff, List<RuleViolation> violations, IEnumerable<string> notes)
    {
        var rows = new List<ImpactRow>();
        foreach (var line in ctx.Lines)
        {
            int? previous = null;
            foreach (var step in ctx.Steps)
            {
                var before = diff.Before.GetValueOrDefault((line.Id, step.Seq)) ?? new Balance(0, 0);
                var after = diff.After[(line.Id, step.Seq)];
                var changed = before != after;
                if (changed)
                {
                    var limit = step.Type == ProcessType.Vendor ? previous ?? line.Qty : previous;
                    var subject = $"{ctx.Label(line)} · langkah {step.Seq} ({step.Name})";
                    var metric = step.Type == ProcessType.Vendor ? "Terkirim" : "Kumulatif";
                    var beforeValue = step.Type == ProcessType.Vendor ? before.Sent : before.Qty;
                    var afterValue = step.Type == ProcessType.Vendor ? after.Sent : after.Qty;
                    var bad = violations.Any(v => v.Subject.StartsWith(subject, StringComparison.Ordinal));
                    rows.Add(new ImpactRow(subject, metric, beforeValue, afterValue, limit, !bad,
                        bad ? violations.First(v => v.Subject.StartsWith(subject, StringComparison.Ordinal)).Message : null));
                    if (step.Type == ProcessType.Vendor && before.Qty != after.Qty)
                    {
                        rows.Add(new ImpactRow(subject, "Diterima", before.Qty, after.Qty, after.Sent, after.Qty <= after.Sent, null));
                    }
                }

                previous = after.Qty;
            }
        }

        // Pelanggaran pada sel yang tidak berubah (hilir) tetap ditampilkan.
        foreach (var v in violations.Where(v => rows.All(r => !v.Subject.StartsWith(r.Subject, StringComparison.Ordinal))))
        {
            rows.Add(new ImpactRow(v.Subject, "Kumulatif", null, v.Current, v.Limit, false, v.Message));
        }

        return new ImpactPreview(rows, violations.Count == 0, violations, notes.ToList());
    }
}
