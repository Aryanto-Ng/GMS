using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Master;
using Gms.Domain.Production;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Microsoft.EntityFrameworkCore;
using Gms.Api.Features.Notifications;

namespace Gms.Api.Features.Production;

internal static class CellRules
{
    public const int MaxQty = 1_000_000;

    /// <summary>Memvalidasi sel (baris WO milik WO, tanpa duplikat, qty 0..maks atau tidak nol bila negatif diizinkan). Mengembalikan sel bernilai.</summary>
    public static List<(WoLine Line, int Qty)> Parse(
        IReadOnlyList<WoLineQty>? cells, WoContext ctx, bool allowNegative, string prefix, ErrorBag errors)
    {
        var result = new List<(WoLine, int)>();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < (cells?.Count ?? 0); i++)
        {
            var cell = cells![i];
            var path = $"{prefix}[{i}]";
            var line = ctx.Line(cell.WoLineId);
            if (line is null)
            {
                errors.Add($"{path}.woLineId", "Sel tidak termasuk WO ini.");
                continue;
            }

            if (!seen.Add(cell.WoLineId))
            {
                errors.Add($"{path}.woLineId", "Sel yang sama muncul lebih dari sekali.");
                continue;
            }

            var invalid = allowNegative ? Math.Abs((long)cell.Qty) > MaxQty : cell.Qty is < 0 or > MaxQty;
            if (invalid)
            {
                errors.Add($"{path}.qty", allowNegative
                    ? $"Qty harus bilangan bulat antara -{MaxQty:N0} dan {MaxQty:N0}."
                    : $"Qty harus bilangan bulat 0 sampai {MaxQty:N0}.");
                continue;
            }

            if (cell.Qty != 0)
            {
                result.Add((line, cell.Qty));
            }
        }

        return result;
    }

    public static async Task<Worker> RequireWorkerAsync(GmsDbContext db, Guid workerId, bool mustBeActive, string path, CancellationToken ct)
    {
        var worker = await db.Workers.AsNoTracking().SingleOrDefaultAsync(w => w.Id == workerId, ct)
                     ?? throw ProblemException.Validation(path, "Pekerja tidak ditemukan.");
        if (mustBeActive && !worker.IsActive)
        {
            throw ProblemException.Validation(path, $"Pekerja {worker.Code} nonaktif.");
        }

        return worker;
    }
}

// ===== Konteks input =====

/// <summary>Batas tersedia per sel untuk layar input (AC-PR-04). Hasil hitung server; tidak mengunci.</summary>
public sealed class GetInputContextHandler(GmsDbContext db, StepLedger ledger)
{
    public async Task<InputContextDto> HandleAsync(Guid woId, int seq, DateOnly workDate, CancellationToken ct)
    {
        var wo = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(w => w.Id == woId, ct) ?? throw ProblemException.NotFound("WO");
        var steps = await db.WoRoutingSnapshots.AsNoTracking().Where(s => s.WoId == woId).OrderBy(s => s.Seq).ToListAsync(ct);
        var stepIndex = steps.FindIndex(s => s.Seq == seq);
        if (stepIndex < 0)
        {
            throw ProblemException.Validation("seq", "Langkah tidak ada pada routing WO ini.");
        }

        var lines = await db.WoLines.AsNoTracking().Where(l => l.WoId == woId).OrderBy(l => l.ColorCode).ThenBy(l => l.SizeCode).ToListAsync(ct);
        var lineIds = lines.Select(l => l.Id).ToList();
        var balances = await db.WoStepBalances.AsNoTracking().Where(b => lineIds.Contains(b.WoLineId)).ToListAsync(ct);
        int Qty(Guid lineId, int s) => balances.FirstOrDefault(b => b.WoLineId == lineId && b.Seq == s)?.Qty ?? 0;

        var sizes = await db.PoLines.AsNoTracking().Where(p => lines.Select(l => l.PoLineId).Contains(p.Id))
            .Join(db.Sizes, p => p.SizeId, s => s.Id, (p, s) => new { p.Id, p.ColorId, p.SizeId, s.SortOrder }).ToDictionaryAsync(x => x.Id, ct);

        var first = stepIndex == 0;
        var previousSeq = first ? (int?)null : steps[stepIndex - 1].Seq;
        var columns = lines.Select(l =>
        {
            var current = Qty(l.Id, seq);
            int? max = previousSeq is { } p ? Math.Max(Qty(l.Id, p) - current, 0) : null;
            var info = sizes[l.PoLineId];
            return new InputColumn(l.Id, new ColorSizeRef(info.ColorId, l.ColorCode, l.ColorName, info.SizeId, l.SizeCode, l.SizeName, info.SortOrder),
                max, l.Qty, current);
        }).ToList();

        string? blocked = null;
        if (wo.Status is not (WoStatus.Released or WoStatus.InProgress))
        {
            blocked = StepLedger.DisabledReasonFor(wo.Status);
        }
        else if (steps[stepIndex].ProcessType != ProcessType.Internal)
        {
            blocked = "Langkah ini bertipe Vendor; gunakan Kirim/Terima Vendor.";
        }
        else if (workDate > ledger.Today)
        {
            blocked = "Tanggal kerja tidak boleh di masa depan.";
        }
        else if (await ledger.IsInClosedPeriodAsync(workDate, ct))
        {
            blocked = "Periode gaji sudah ditutup, gunakan Penyesuaian.";
        }

        var warnings = new List<string>();
        if (first)
        {
            warnings.Add("Langkah pertama tanpa batas atas; kelebihan terhadap Qty WO akan meminta konfirmasi.");
        }

        var current2 = columns.Sum(c => c.CurrentCumulative);
        return new InputContextDto(
            woId, wo.Code, seq, steps[stepIndex].ProcessName, workDate,
            first ? columns.Sum(c => c.WoQty) : columns.Sum(c => Qty(c.WoLineId, previousSeq!.Value)),
            current2,
            first ? columns.Sum(c => Math.Max(c.WoQty - c.CurrentCumulative, 0)) : columns.Sum(c => c.MaxAllowed ?? 0),
            first, columns, warnings, blocked);
    }
}

// ===== Simpan output =====

/// <summary>Satu penyimpanan, banyak pekerja, semua-atau-tidak-sama-sekali (AC-PR-01a). Satu entri per pekerja.</summary>
public sealed class SaveOutputsHandler(GmsDbContext db, StepLedger ledger, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task<SaveOutputsResult> HandleAsync(SaveOutputsRequest request, CancellationToken ct)
    {
        var rows = request.Rows ?? [];
        var errors = new ErrorBag();
        var seenWorkers = new HashSet<Guid>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!seenWorkers.Add(rows[i].WorkerId))
            {
                errors.Add($"rows[{i}].workerId", "Pekerja yang sama muncul lebih dari sekali dalam satu penyimpanan.");
            }
        }

        errors.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await ledger.BeginAsync(request.WoId, [request.WorkDate], ct);
        StepLedger.RequireOpenForEntries(ctx.Wo);
        await ledger.EnsureWorkDateAllowedAsync(request.WorkDate, "workDate", ct);

        var step = ctx.Step(request.Seq) ?? throw ProblemException.Validation("seq", "Langkah tidak ada pada routing WO ini.");
        if (step.Type != ProcessType.Internal)
        {
            throw ProblemException.Validation("seq", "Langkah bertipe Vendor dicatat lewat Kirim/Terima Vendor.");
        }

        var parsed = new List<(OutputRow Row, List<(WoLine Line, int Qty)> Cells)>();
        for (var i = 0; i < rows.Count; i++)
        {
            await CellRules.RequireWorkerAsync(db, rows[i].WorkerId, true, $"rows[{i}].workerId", ct);
            var cells = CellRules.Parse(rows[i].Cells, ctx, false, $"rows[{i}].cells", errors);
            if (cells.Count > 0)
            {
                parsed.Add((rows[i], cells));
            }
        }

        errors.ThrowIfAny();
        if (parsed.Count == 0)
        {
            throw ProblemException.Validation("rows", "Tidak ada baris dengan qty lebih dari 0.");
        }

        var now = time.GetUtcNow();
        var entries = new List<OutputEntry>();
        foreach (var (row, cells) in parsed)
        {
            var entry = new OutputEntry
            {
                WoId = ctx.Wo.Id, Seq = request.Seq, WorkerId = row.WorkerId, WorkDate = request.WorkDate,
                Kind = OutputKind.Output, CreatedAt = now, CreatedBy = user.Id,
            };
            foreach (var (line, qty) in cells)
            {
                entry.Lines.Add(new OutputEntryLine { EntryId = entry.Id, WoLineId = line.Id, Qty = qty });
            }

            entries.Add(entry);
        }

        db.OutputEntries.AddRange(entries);
        await db.SaveChangesAsync(ct);

        var diff = await ledger.RecomputeAsync(ctx, ct);
        StepLedger.ThrowIfAny(await ledger.ValidateAsync(ctx, diff.After, new HashSet<int> { request.Seq }, ct));

        var warnings = new List<string>();
        if (ctx.Steps[0].Seq == request.Seq)
        {
            var over = ctx.Lines.Select(l => (Line: l, Cum: diff.After[(l.Id, request.Seq)].Qty)).Where(x => x.Cum > x.Line.Qty).ToList();
            if (over.Count > 0)
            {
                var violations = over
                    .Select(x => new RuleViolation(ProblemCodes.OutputExceedsWoQtyNeedsConfirmation, ctx.Label(x.Line), x.Cum, x.Line.Qty,
                        $"{ctx.Label(x.Line)}: kumulatif langkah pertama {x.Cum} melebihi Qty WO ({x.Line.Qty})."))
                    .ToList();
                if (!request.ConfirmExceedWoQty)
                {
                    throw ProblemException.Rule(ProblemCodes.OutputExceedsWoQtyNeedsConfirmation,
                        "Output melebihi Qty WO (kelebihan produksi). Konfirmasi untuk melanjutkan.", violations);
                }

                warnings.AddRange(violations.Select(v => v.Message));
            }
        }

        await ledger.MarkInProgressAsync(ctx, user.Id, ct);
        var total = parsed.Sum(p => p.Cells.Sum(c => c.Qty));
        audit.Add(AuditEvents.OutputCreated, "WorkOrder", ctx.Wo.Id.ToString(), null,
            new { seq = request.Seq, workDate = request.WorkDate, workers = parsed.Count, totalQty = total, entryIds = entries.Select(e => e.Id) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new SaveOutputsResult(entries.Select(e => e.Id).ToList(), total, parsed.Count, warnings);
    }
}

// ===== Void =====

/// <summary>Void entri Output/Penyesuaian dengan alasan wajib; aturan kumulatif dicek pada kondisi akhir (AC-PR-11..14).</summary>
public sealed class VoidOutputHandler(GmsDbContext db, StepLedger ledger, ICurrentUser user, IAuditWriter audit, TimeProvider time, NotificationEmitter notify)
{
    public async Task<ImpactPreview> PreviewAsync(Guid id, CancellationToken ct) => (await RunAsync(id, null, dryRun: true, ct))!;

    public async Task HandleAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            throw ProblemException.Validation("reason", "Alasan void wajib diisi (maksimal 500 karakter).");
        }

        await RunAsync(id, reason, dryRun: false, ct);
    }

    private async Task<ImpactPreview?> RunAsync(Guid id, string? reason, bool dryRun, CancellationToken ct)
    {
        var info = await db.OutputEntries.AsNoTracking().Where(e => e.Id == id).Select(e => new { e.WoId, e.WorkDate }).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Entri");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await ledger.BeginAsync(info.WoId, [info.WorkDate], ct);
        StepLedger.RequireOpenForEntries(ctx.Wo);

        var entry = await db.OutputEntries.SingleAsync(e => e.Id == id, ct);
        if (entry.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Entri sudah di-void dan tidak dapat diaktifkan kembali.");
        }

        if (await ledger.IsInClosedPeriodAsync(entry.WorkDate, ct))
        {
            throw ProblemException.PeriodClosed("Tanggal kerja entri berada pada periode gaji yang sudah ditutup. Gunakan Penyesuaian.");
        }

        entry.Status = DocStatus.Voided;
        entry.VoidReason = reason ?? "(pratinjau)";
        entry.VoidedAt = time.GetUtcNow();
        entry.VoidedBy = user.Id;
        await db.SaveChangesAsync(ct);

        var diff = await ledger.RecomputeAsync(ctx, ct);
        var violations = await ledger.ValidateAsync(ctx, diff.After, new HashSet<int> { entry.Seq }, ct);
        if (dryRun)
        {
            var preview = StepLedger.BuildPreview(ctx, diff, violations, ["Entri akan di-void dan tidak dihitung pada kumulatif maupun upah."]);
            await tx.RollbackAsync(ct);
            return preview;
        }

        StepLedger.ThrowIfAny(violations);
        await notify.OutputChangedAsync(entry, "void", ct);
        audit.Add(AuditEvents.OutputVoided, "OutputEntry", id.ToString(), new { status = "Active" }, new { status = "Voided", reason, kind = entry.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }
}

// ===== Koreksi =====

/// <summary>Koreksi = void entri asal + entri baru dalam satu transaksi; aturan kumulatif hanya pada kondisi akhir (AC-PR-15..17).</summary>
public sealed class CorrectOutputHandler(GmsDbContext db, StepLedger ledger, ICurrentUser user, IAuditWriter audit, TimeProvider time, NotificationEmitter notify)
{
    public async Task<ImpactPreview> PreviewAsync(Guid id, CorrectOutputRequest request, CancellationToken ct) => (await RunAsync(id, request, dryRun: true, ct)).Preview!;

    public async Task<Guid> HandleAsync(Guid id, CorrectOutputRequest request, CancellationToken ct) =>
        (await RunAsync(id, request, dryRun: false, ct)).NewId;

    private async Task<(Guid NewId, ImpactPreview? Preview)> RunAsync(Guid id, CorrectOutputRequest request, bool dryRun, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            throw ProblemException.Validation("reason", "Alasan koreksi wajib diisi (maksimal 500 karakter).");
        }

        var info = await db.OutputEntries.AsNoTracking().Where(e => e.Id == id).Select(e => new { e.WoId, e.WorkDate }).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Entri");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await ledger.BeginAsync(info.WoId, [info.WorkDate, request.WorkDate], ct);
        StepLedger.RequireOpenForEntries(ctx.Wo);

        var entry = await db.OutputEntries.SingleAsync(e => e.Id == id, ct);
        if (entry.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Entri yang sudah di-void tidak dapat dikoreksi.");
        }

        if (entry.Kind != OutputKind.Output)
        {
            throw ProblemException.StateConflict("Penyesuaian tidak dapat dikoreksi. Void lalu buat Penyesuaian baru.");
        }

        if (await ledger.IsInClosedPeriodAsync(entry.WorkDate, ct))
        {
            throw ProblemException.PeriodClosed("Tanggal kerja entri berada pada periode gaji yang sudah ditutup. Gunakan Penyesuaian.");
        }

        await ledger.EnsureWorkDateAllowedAsync(request.WorkDate, "workDate", ct);
        await CellRules.RequireWorkerAsync(db, request.WorkerId, true, "workerId", ct);

        var errors = new ErrorBag();
        var cells = CellRules.Parse(request.Cells, ctx, false, "cells", errors);
        errors.ThrowIfAny();
        if (cells.Count == 0)
        {
            throw ProblemException.Validation("cells", "Minimal satu sel harus memiliki qty lebih dari 0.");
        }

        var now = time.GetUtcNow();
        var before = new { entry.WorkerId, entry.WorkDate };
        entry.Status = DocStatus.Voided;
        entry.VoidReason = $"Dikoreksi: {reason}";
        entry.VoidedAt = now;
        entry.VoidedBy = user.Id;

        var replacement = new OutputEntry
        {
            WoId = ctx.Wo.Id, Seq = entry.Seq, WorkerId = request.WorkerId, WorkDate = request.WorkDate, Kind = OutputKind.Output,
            Reason = reason, ReplacesEntryId = entry.Id, CreatedAt = now, CreatedBy = user.Id,
        };
        foreach (var (line, qty) in cells)
        {
            replacement.Lines.Add(new OutputEntryLine { EntryId = replacement.Id, WoLineId = line.Id, Qty = qty });
        }

        db.OutputEntries.Add(replacement);
        await db.SaveChangesAsync(ct);

        var diff = await ledger.RecomputeAsync(ctx, ct);
        var violations = await ledger.ValidateAsync(ctx, diff.After, new HashSet<int> { entry.Seq }, ct);
        if (dryRun)
        {
            var preview = StepLedger.BuildPreview(ctx, diff, violations, ["Entri asal di-void dan diganti entri baru dalam satu transaksi."]);
            await tx.RollbackAsync(ct);
            return (Guid.Empty, preview);
        }

        StepLedger.ThrowIfAny(violations);
        await notify.OutputChangedAsync(entry, "correct", ct);
        audit.Add(AuditEvents.OutputCorrected, "OutputEntry", id.ToString(), before,
            new { replacedBy = replacement.Id, request.WorkerId, request.WorkDate, reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (replacement.Id, null);
    }
}

// ===== Penyesuaian =====

/// <summary>Penyesuaian untuk tanggal asal di periode gaji tertutup; qty boleh negatif; upah masuk periode terbuka (AC-PR-18..21).</summary>
public sealed class CreateAdjustmentHandler(GmsDbContext db, StepLedger ledger, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task<Guid> HandleAsync(AdjustmentRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        var errors = new ErrorBag();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            errors.Add("reason", "Alasan penyesuaian wajib diisi (maksimal 500 karakter).");
        }

        errors.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await ledger.BeginAsync(request.WoId, [request.OriginalDate, request.WorkDate], ct);
        StepLedger.RequireOpenForEntries(ctx.Wo);

        var step = ctx.Step(request.Seq) ?? throw ProblemException.Validation("seq", "Langkah tidak ada pada routing WO ini.");
        if (step.Type != ProcessType.Internal)
        {
            throw ProblemException.Validation("seq", "Penyesuaian hanya untuk langkah Internal (berpekerja).");
        }

        if (request.OriginalDate > ledger.Today || !await ledger.IsInClosedPeriodAsync(request.OriginalDate, ct))
        {
            throw ProblemException.Validation("originalDate",
                "Tanggal Asal harus berada pada periode gaji yang sudah ditutup. Untuk periode terbuka gunakan Void atau Koreksi.");
        }

        await ledger.EnsureWorkDateAllowedAsync(request.WorkDate, "workDate", ct);
        await CellRules.RequireWorkerAsync(db, request.WorkerId, mustBeActive: false, "workerId", ct); // pekerja nonaktif diizinkan

        var cells = CellRules.Parse(request.Cells, ctx, allowNegative: true, "cells", errors);
        errors.ThrowIfAny();
        if (cells.Count == 0)
        {
            throw ProblemException.Validation("cells", "Minimal satu sel harus bernilai selain 0.");
        }

        var entry = new OutputEntry
        {
            WoId = ctx.Wo.Id, Seq = request.Seq, WorkerId = request.WorkerId, WorkDate = request.WorkDate, Kind = OutputKind.Adjustment,
            OriginalDate = request.OriginalDate, Reason = reason, CreatedAt = time.GetUtcNow(), CreatedBy = user.Id,
        };
        foreach (var (line, qty) in cells)
        {
            entry.Lines.Add(new OutputEntryLine { EntryId = entry.Id, WoLineId = line.Id, Qty = qty });
        }

        db.OutputEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        var diff = await ledger.RecomputeAsync(ctx, ct);
        StepLedger.ThrowIfAny(await ledger.ValidateAsync(ctx, diff.After, new HashSet<int> { request.Seq }, ct));

        audit.Add(AuditEvents.OutputAdjusted, "OutputEntry", entry.Id.ToString(), null,
            new { request.WoId, request.Seq, request.WorkerId, request.OriginalDate, request.WorkDate, qty = cells.Sum(c => c.Qty), reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return entry.Id;
    }
}
