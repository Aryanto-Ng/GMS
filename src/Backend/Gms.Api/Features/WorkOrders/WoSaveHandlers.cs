using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Features.Sales;
using Gms.Api.Security;
using Gms.Domain.Production;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.WorkOrders;

internal static class WoValidation
{
    public const int MaxQty = 1_000_000;

    public static void ValidateHeader(DateOnly? start, DateOnly? target, string? notes, ErrorBag errors)
    {
        if (start is not null && target is not null && target < start)
        {
            errors.Add("targetDate", "Target Selesai tidak boleh sebelum Tanggal Mulai.");
        }

        if (notes is { Length: > 1000 })
        {
            errors.Add("notes", "Keterangan maksimal 1000 karakter.");
        }
    }

    public static void EnsureActivePo(PurchaseOrder po)
    {
        if (po.Status is not (PoStatus.Confirmed or PoStatus.InProgress))
        {
            throw ProblemException.StateConflict($"WO hanya dapat dibuat/diubah untuk PO berstatus Confirmed atau In Progress (status PO: {po.Status}).");
        }
    }

    /// <summary>Menolak bila ada kombinasi yang melebihi Sisa Rencana. <paramref name="own"/> = qty WO ini yang sudah terhitung di Dalam Produksi.</summary>
    public static async Task EnsureWithinRemainingPlanAsync(
        GmsDbContext db, IEnumerable<(PoLine PoLine, int NewQty)> requested, IReadOnlyDictionary<Guid, int> own, CancellationToken ct)
    {
        var violations = new List<RuleViolation>();
        foreach (var (poLine, newQty) in requested)
        {
            var limit = poLine.RemainingPlanQty + own.GetValueOrDefault(poLine.Id);
            if (newQty > limit)
            {
                var label = await Label(db, poLine, ct);
                var message = $"{label}: Qty WO {newQty} melebihi Sisa Rencana PO ({limit}).";
                violations.Add(new RuleViolation(ProblemCodes.PoRemainingPlanExceeded, label, newQty, limit, message));
            }
        }

        if (violations.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.PoRemainingPlanExceeded,
                "Qty WO melebihi Sisa Rencana PO. Sisa terbaru ditampilkan pada setiap sel.", violations);
        }
    }

    public static async Task<string> Label(GmsDbContext db, PoLine poLine, CancellationToken ct)
    {
        var color = await db.Colors.AsNoTracking().Where(c => c.Id == poLine.ColorId).Select(c => c.Code).SingleAsync(ct);
        var size = await db.Sizes.AsNoTracking().Where(s => s.Id == poLine.SizeId).Select(s => s.Code).SingleAsync(ct);
        return $"{color} / {size}";
    }

    public static void AddHistory(GmsDbContext db, Guid woId, WoStatus? from, WoStatus to, string? reason, DateTimeOffset now, Guid? by) =>
        db.WoStatusHistory.Add(new WoStatusHistory { WoId = woId, FromStatus = from, ToStatus = to, Reason = reason, ChangedAt = now, ChangedBy = by });

    public static async Task<(string ColorCode, string ColorName, string SizeCode, string SizeName)> LabelsAsync(
        GmsDbContext db, PoLine poLine, CancellationToken ct)
    {
        var color = await db.Colors.AsNoTracking().Where(c => c.Id == poLine.ColorId).Select(c => new { c.Code, c.Name }).SingleAsync(ct);
        var size = await db.Sizes.AsNoTracking().Where(s => s.Id == poLine.SizeId).Select(s => new { s.Code, s.Name }).SingleAsync(ct);
        return (color.Code, color.Name, size.Code, size.Name);
    }

    /// <summary>Validasi baris grid Draft: qty 0..maks, tanpa duplikat, minimal satu &gt; 0. Mengembalikan hanya baris dengan qty &gt; 0.</summary>
    public static List<SaveWoLine> ValidateDraftLines(IReadOnlyList<SaveWoLine>? lines, ErrorBag errors)
    {
        lines ??= [];
        var seen = new HashSet<Guid>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Qty is < 0 or > MaxQty)
            {
                errors.Add($"lines[{i}].qty", $"Qty harus bilangan bulat 0 sampai {MaxQty:N0}.");
            }

            if (!seen.Add(lines[i].PoLineId))
            {
                errors.Add($"lines[{i}].poLineId", "Baris PO yang sama muncul lebih dari sekali.");
            }
        }

        var positive = lines.Where(l => l.Qty > 0).ToList();
        if (positive.Count == 0)
        {
            errors.Add("lines", "Minimal satu sel harus memiliki Qty lebih dari 0.");
        }

        return positive;
    }
}

/// <summary>Membuat WO Draft dari PO + FG (AC-WO-01..08). Kode otomatis; lock baris PO agar dua pembuat bersamaan tidak melampaui Sisa Rencana.</summary>
public sealed class CreateWorkOrderHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, NumberSequences sequences, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(SaveWoRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        WoValidation.ValidateHeader(request.StartDate, request.TargetDate, request.Notes, errors);
        var lines = WoValidation.ValidateDraftLines(request.Lines, errors);
        errors.ThrowIfAny();

        var fg = await db.Fgs.AsNoTracking().Where(f => f.Id == request.FgId).Select(f => new { f.Id, f.Code, f.Name }).SingleOrDefaultAsync(ct)
                 ?? throw ProblemException.Validation("fgId", "FG tidak ditemukan.");
        var now = time.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RowLocks.LockPoLinesAsync(db, lines.Select(l => l.PoLineId).ToList(), ct);

        var po = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PoId, ct)
                 ?? throw ProblemException.Validation("poId", "PO tidak ditemukan.");
        WoValidation.EnsureActivePo(po);

        var poLines = await db.PoLines.Where(l => lines.Select(x => x.PoLineId).Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var lineErrors = new ErrorBag();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            if (request.Lines[i].Qty <= 0)
            {
                continue;
            }

            if (!poLines.TryGetValue(request.Lines[i].PoLineId, out var pl) || pl.PoId != request.PoId || pl.FgId != request.FgId)
            {
                lineErrors.Add($"lines[{i}].poLineId", "Baris PO tidak ditemukan pada PO dan FG yang dipilih.");
            }
        }

        lineErrors.ThrowIfAny();
        await WoValidation.EnsureWithinRemainingPlanAsync(db, lines.Select(l => (poLines[l.PoLineId], l.Qty)), new Dictionary<Guid, int>(), ct);

        var wo = new WorkOrder
        {
            Code = await sequences.NextMonthlyCodeAsync("WO", ct), PoId = request.PoId, FgId = request.FgId,
            FgCode = fg.Code, FgName = fg.Name, StartDate = request.StartDate, TargetDate = request.TargetDate,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            Status = WoStatus.Draft, CreatedAt = now, CreatedBy = user.Id,
        };
        foreach (var line in lines)
        {
            var labels = await WoValidation.LabelsAsync(db, poLines[line.PoLineId], ct);
            wo.Lines.Add(new WoLine
            {
                WoId = wo.Id, PoLineId = line.PoLineId, Qty = line.Qty, CreatedAt = now, CreatedBy = user.Id,
                ColorCode = labels.ColorCode, ColorName = labels.ColorName, SizeCode = labels.SizeCode, SizeName = labels.SizeName,
            });
        }

        db.WorkOrders.Add(wo);
        WoValidation.AddHistory(db, wo.Id, null, WoStatus.Draft, null, now, user.Id);
        audit.Add(AuditEvents.WoCreated, "WorkOrder", wo.Id.ToString(), null,
            new { wo.Code, poId = wo.PoId, fgId = wo.FgId, totalQty = lines.Sum(l => l.Qty), lines = lines.Count });
        await db.SaveChangesAsync(ct);
        await PoLifecycle.RecomputeInProductionAsync(db, lines.Select(l => l.PoLineId).ToList(), ct);
        await tx.CommitAsync(ct);

        return await queries.GetDetailAsync(wo.Id, ct);
    }
}

/// <summary>Mengubah WO Draft: Qty, tanggal, keterangan (AC-WO-06). PO dan FG tidak berubah (batalkan dan buat WO baru).</summary>
public sealed class UpdateDraftWorkOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(Guid id, SaveWoRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        WoValidation.ValidateHeader(request.StartDate, request.TargetDate, request.Notes, errors);
        var lines = WoValidation.ValidateDraftLines(request.Lines, errors);
        errors.ThrowIfAny();

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existingLineIds = await db.WoLines.AsNoTracking().Where(l => l.WoId == id).Select(l => l.PoLineId).ToListAsync(ct);
        if (existingLineIds.Count == 0 && !await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            throw ProblemException.NotFound("WO");
        }

        // Urutan kunci: work_orders → po_lines (ADR-004), sebelum memuat entitas.
        await RowLocks.LockWorkOrderAsync(db, id, ct);
        var allPoLineIds = existingLineIds.Concat(lines.Select(l => l.PoLineId)).Distinct().ToList();
        await RowLocks.LockPoLinesAsync(db, allPoLineIds, ct);

        var wo = await db.WorkOrders.Include(w => w.Lines).SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw ProblemException.NotFound("WO");
        db.Entry(wo).Property(w => w.RowVersion).OriginalValue = request.RowVersion;
        if (wo.Status != WoStatus.Draft)
        {
            throw ProblemException.StateConflict($"Hanya WO Draft yang dapat diubah bebas (status saat ini: {wo.Status}). Setelah dirilis gunakan perubahan Qty.");
        }

        var identityErrors = new ErrorBag();
        if (request.PoId != wo.PoId)
        {
            identityErrors.Add("poId", "PO WO tidak dapat diganti. Batalkan WO ini dan buat WO baru.");
        }

        if (request.FgId != wo.FgId)
        {
            identityErrors.Add("fgId", "FG WO tidak dapat diganti. Batalkan WO ini dan buat WO baru.");
        }

        identityErrors.ThrowIfAny();

        var po = await db.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == wo.PoId, ct);
        WoValidation.EnsureActivePo(po);

        var poLines = await db.PoLines.Where(l => allPoLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var lineErrors = new ErrorBag();
        for (var i = 0; i < request.Lines.Count; i++)
        {
            if (request.Lines[i].Qty > 0
                && (!poLines.TryGetValue(request.Lines[i].PoLineId, out var pl) || pl.PoId != wo.PoId || pl.FgId != wo.FgId))
            {
                lineErrors.Add($"lines[{i}].poLineId", "Baris PO tidak ditemukan pada PO dan FG WO ini.");
            }
        }

        lineErrors.ThrowIfAny();
        var own = wo.Lines.ToDictionary(l => l.PoLineId, l => Math.Max(l.Qty - l.HandedOverQty, 0));
        await WoValidation.EnsureWithinRemainingPlanAsync(db, lines.Select(l => (poLines[l.PoLineId], l.Qty)), own, ct);

        var before = new { wo.StartDate, wo.TargetDate, wo.Notes, lines = wo.Lines.Select(l => new { l.PoLineId, l.Qty }).ToList() };
        var wanted = lines.ToDictionary(l => l.PoLineId);
        foreach (var current in wo.Lines.ToList())
        {
            if (!wanted.ContainsKey(current.PoLineId))
            {
                db.WoLines.Remove(current);
            }
        }

        foreach (var line in lines)
        {
            var current = wo.Lines.FirstOrDefault(l => l.PoLineId == line.PoLineId);
            var labels = await WoValidation.LabelsAsync(db, poLines[line.PoLineId], ct);
            if (current is null)
            {
                db.WoLines.Add(new WoLine
                {
                    WoId = wo.Id, PoLineId = line.PoLineId, Qty = line.Qty, CreatedAt = now, CreatedBy = user.Id,
                    ColorCode = labels.ColorCode, ColorName = labels.ColorName, SizeCode = labels.SizeCode, SizeName = labels.SizeName,
                });
            }
            else if (current.Qty != line.Qty)
            {
                current.Qty = line.Qty;
                current.UpdatedAt = now;
                current.UpdatedBy = user.Id;
            }
        }

        wo.StartDate = request.StartDate;
        wo.TargetDate = request.TargetDate;
        wo.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        wo.UpdatedAt = now;
        wo.UpdatedBy = user.Id;
        audit.Add(AuditEvents.WoUpdated, "WorkOrder", id.ToString(), before,
            new { wo.StartDate, wo.TargetDate, wo.Notes, lines = lines.Select(l => new { l.PoLineId, l.Qty }).ToList() });

        await db.SaveChangesAsync(ct);
        await PoLifecycle.RecomputeInProductionAsync(db, allPoLineIds, ct);
        await tx.CommitAsync(ct);

        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>
/// Mengubah Qty per sel pada WO Released/In Progress (AC-WO-13..16). Baris tidak dapat ditambah/dihapus; Qty tidak boleh di bawah
/// output kumulatif proses pertama; kenaikan tunduk pada Sisa Rencana PO.
/// </summary>
public sealed class UpdateWoQuantitiesHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(Guid id, UpdateWoQuantitiesRequest request, CancellationToken ct)
    {
        var requested = request.Lines ?? [];
        var errors = new ErrorBag();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < requested.Count; i++)
        {
            if (requested[i].Qty is < 1 or > WoValidation.MaxQty)
            {
                errors.Add($"lines[{i}].qty", $"Qty harus bilangan bulat 1 sampai {WoValidation.MaxQty:N0}.");
            }

            if (!seen.Add(requested[i].PoLineId))
            {
                errors.Add($"lines[{i}].poLineId", "Baris PO yang sama muncul lebih dari sekali.");
            }
        }

        errors.ThrowIfAny();

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var poLineIds = await db.WoLines.AsNoTracking().Where(l => l.WoId == id).Select(l => l.PoLineId).ToListAsync(ct);
        if (poLineIds.Count == 0 && !await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            throw ProblemException.NotFound("WO");
        }

        await RowLocks.LockWorkOrderAsync(db, id, ct);
        await RowLocks.LockPoLinesAsync(db, poLineIds, ct);

        var wo = await db.WorkOrders.Include(w => w.Lines).SingleAsync(w => w.Id == id, ct);
        db.Entry(wo).Property(w => w.RowVersion).OriginalValue = request.RowVersion;
        if (wo.Status is not (WoStatus.Released or WoStatus.InProgress))
        {
            throw ProblemException.StateConflict(
                $"Qty hanya dapat diubah pada WO Released atau In Progress (status saat ini: {wo.Status}).", ProblemCodes.WoStatusInvalid);
        }

        var setErrors = new ErrorBag();
        for (var i = 0; i < requested.Count; i++)
        {
            if (wo.Lines.All(l => l.PoLineId != requested[i].PoLineId))
            {
                setErrors.Add($"lines[{i}].poLineId", "Baris tidak termasuk WO ini. Baris tidak dapat ditambah setelah WO dirilis; buat WO baru.");
            }
        }

        if (requested.Count != wo.Lines.Count)
        {
            setErrors.Add("lines", "Kirim seluruh baris WO. Baris tidak dapat ditambah atau dihapus setelah WO dirilis.");
        }

        setErrors.ThrowIfAny();

        var firstSeq = await db.WoRoutingSnapshots.Where(r => r.WoId == id).MinAsync(r => (int?)r.Seq, ct);
        var lineIds = wo.Lines.Select(l => l.Id).ToList();
        var firstOutput = firstSeq is { } fs
            ? await db.WoStepBalances.Where(b => lineIds.Contains(b.WoLineId) && b.Seq == fs).ToDictionaryAsync(b => b.WoLineId, b => b.Qty, ct)
            : [];

        var poLines = await db.PoLines.Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var below = new List<RuleViolation>();
        var increases = new List<(PoLine PoLine, int NewQty)>();
        var own = new Dictionary<Guid, int>();
        foreach (var req in requested)
        {
            var line = wo.Lines.Single(l => l.PoLineId == req.PoLineId);
            var minimum = Math.Max(1, firstOutput.GetValueOrDefault(line.Id));
            if (req.Qty < minimum)
            {
                var label = $"{line.ColorCode} / {line.SizeCode}";
                var message = $"{label}: Qty {req.Qty} di bawah output kumulatif proses pertama ({minimum}).";
                below.Add(new RuleViolation(ProblemCodes.WoQtyBelowOutput, label, req.Qty, minimum, message));
            }

            if (req.Qty > line.Qty)
            {
                // Kontribusi ke Dalam Produksi = qty − diserahterimakan; bagian WO ini yang sudah terhitung dikembalikan ke batas.
                increases.Add((poLines[line.PoLineId], Math.Max(req.Qty - line.HandedOverQty, 0)));
                own[line.PoLineId] = Math.Max(line.Qty - line.HandedOverQty, 0);
            }
        }

        if (below.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.WoQtyBelowOutput, "Qty WO tidak boleh lebih kecil dari output yang sudah dicatat.", below);
        }

        await WoValidation.EnsureWithinRemainingPlanAsync(db, increases, own, ct);

        var before = new List<object>();
        var after = new List<object>();
        foreach (var req in requested)
        {
            var line = wo.Lines.Single(l => l.PoLineId == req.PoLineId);
            if (line.Qty == req.Qty)
            {
                continue;
            }

            before.Add(new { line.PoLineId, line.Qty });
            after.Add(new { line.PoLineId, req.Qty });
            line.Qty = req.Qty;
            line.UpdatedAt = now;
            line.UpdatedBy = user.Id;
        }

        wo.UpdatedAt = now;
        wo.UpdatedBy = user.Id;
        audit.Add(AuditEvents.WoQuantitiesChanged, "WorkOrder", id.ToString(), new { lines = before }, new { lines = after });
        await db.SaveChangesAsync(ct);
        await PoLifecycle.RecomputeInProductionAsync(db, poLineIds, ct);
        await tx.CommitAsync(ct);

        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>Mengubah Tanggal Mulai, Target Selesai, Keterangan pada WO Draft/Released/In Progress (AC-WO-13).</summary>
public sealed class UpdateWoScheduleHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(Guid id, UpdateWoScheduleRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        WoValidation.ValidateHeader(request.StartDate, request.TargetDate, request.Notes, errors);
        errors.ThrowIfAny();

        var wo = await db.WorkOrders.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw ProblemException.NotFound("WO");
        db.Entry(wo).Property(w => w.RowVersion).OriginalValue = request.RowVersion;
        if (wo.Status is not (WoStatus.Draft or WoStatus.Released or WoStatus.InProgress))
        {
            throw ProblemException.StateConflict($"Jadwal tidak dapat diubah pada WO berstatus {wo.Status}.", ProblemCodes.WoStatusInvalid);
        }

        var before = new { wo.StartDate, wo.TargetDate, wo.Notes };
        wo.StartDate = request.StartDate;
        wo.TargetDate = request.TargetDate;
        wo.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        wo.UpdatedAt = time.GetUtcNow();
        wo.UpdatedBy = user.Id;
        audit.Add(AuditEvents.WoUpdated, "WorkOrder", id.ToString(), before, new { wo.StartDate, wo.TargetDate, wo.Notes });
        await db.SaveChangesAsync(ct);

        return await queries.GetDetailAsync(id, ct);
    }
}
