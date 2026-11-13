using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Features.Master;
using Gms.Api.Features.Sales;
using Gms.Api.Security;
using Gms.Domain.Production;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Gms.Api.Features.Notifications;

namespace Gms.Api.Features.WorkOrders;

/// <summary>Hasil evaluasi syarat rilis (AC-WO-09) yang dipakai pratinjau dan rilis sebenarnya.</summary>
internal sealed record WoReleaseEvaluation(List<ReadinessItem> Checks, int StepCount, int TariffCellCount);

internal static class WoRelease
{
    public static async Task<WoReleaseEvaluation> EvaluateAsync(GmsDbContext db, WorkOrder wo, CancellationToken ct)
    {
        var checks = new List<ReadinessItem>();

        var poStatus = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == wo.PoId).Select(p => p.Status).SingleAsync(ct);
        var poOk = poStatus is PoStatus.Confirmed or PoStatus.InProgress;
        checks.Add(new ReadinessItem("po.status", poOk,
            poOk ? $"PO berstatus {poStatus}." : $"PO berstatus {poStatus}; WO hanya dapat dirilis bila PO Confirmed atau In Progress.", "po"));

        var (_, fgItems) = await FgReadiness.EvaluateAsync(db, wo.FgId, ct);
        checks.AddRange(fgItems);

        var lines = await db.WoLines.AsNoTracking().Where(l => l.WoId == wo.Id).ToListAsync(ct);
        checks.Add(new ReadinessItem("wo.lines", lines.Any(l => l.Qty > 0),
            lines.Any(l => l.Qty > 0) ? $"{lines.Count} sel dengan Qty." : "WO belum memiliki baris dengan Qty lebih dari 0.", "lines"));

        var poLineIds = lines.Select(l => l.PoLineId).ToList();
        var poLines = await db.PoLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var over = lines
            .Where(l => l.Qty > poLines[l.PoLineId].RemainingPlanQty + Math.Max(l.Qty - l.HandedOverQty, 0))
            .Select(l => $"{l.ColorCode}/{l.SizeCode}").ToList();
        checks.Add(new ReadinessItem("wo.remaining-plan", over.Count == 0,
            over.Count == 0 ? "Qty seluruh sel masih dalam Sisa Rencana PO." : $"Qty melebihi Sisa Rencana PO pada: {string.Join(", ", over)}.", "lines"));

        var steps = await db.FgRoutingSteps.AsNoTracking().Where(s => s.FgId == wo.FgId)
            .Join(db.Processes, s => s.ProcessId, p => p.Id, (s, p) => p.Type).ToListAsync(ct);
        var sizeCount = lines.Select(l => poLines[l.PoLineId].SizeId).Distinct().Count();
        return new WoReleaseEvaluation(checks, steps.Count, steps.Count(t => t == ProcessType.Internal) * sizeCount);
    }
}

/// <summary>Persiapan umum aksi status: kunci WO → baris PO (ADR-004), muat WO, pasang rowVersion klien. Harus dalam transaksi.</summary>
internal static class WoFlow
{
    public static string? RequireReason(string? reason, bool required, string aksi)
    {
        var text = reason?.Trim();
        if (required && string.IsNullOrEmpty(text))
        {
            throw ProblemException.Validation("reason", $"Alasan {aksi} wajib diisi.");
        }

        if (text is { Length: > 500 })
        {
            throw ProblemException.Validation("reason", "Alasan maksimal 500 karakter.");
        }

        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static async Task<(WorkOrder Wo, List<Guid> PoLineIds)> LoadLockedAsync(
        GmsDbContext db, Guid id, uint rowVersion, bool lockPoLines, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            throw ProblemException.NotFound("WO");
        }

        await RowLocks.LockWorkOrderAsync(db, id, ct);
        var poLineIds = await db.WoLines.AsNoTracking().Where(l => l.WoId == id).Select(l => l.PoLineId).ToListAsync(ct);
        if (lockPoLines)
        {
            await RowLocks.LockPoLinesAsync(db, poLineIds, ct);
        }

        var wo = await db.WorkOrders.Include(w => w.Lines).SingleAsync(w => w.Id == id, ct);
        db.Entry(wo).Property(w => w.RowVersion).OriginalValue = rowVersion;
        return (wo, poLineIds);
    }

    public static void Require(WorkOrder wo, string aksi, params WoStatus[] allowed)
    {
        if (!allowed.Contains(wo.Status))
        {
            throw ProblemException.StateConflict(
                $"{aksi} tidak dapat dilakukan pada WO berstatus {wo.Status}. Muat ulang halaman.", ProblemCodes.WoStatusInvalid);
        }
    }

    public static void Transition(GmsDbContext db, WorkOrder wo, WoStatus to, string? reason, DateTimeOffset now, Guid? by)
    {
        WoValidation.AddHistory(db, wo.Id, wo.Status, to, reason, now, by);
        wo.Status = to;
        wo.UpdatedAt = now;
        wo.UpdatedBy = by;
    }
}

/// <summary>Rilis WO: Draft → Released dengan snapshot routing, tarif, dan label (AC-WO-09..12).</summary>
public sealed class ReleaseWorkOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries, NotificationEmitter notify)
{
    public async Task<WoDto> HandleAsync(Guid id, WoActionRequest request, CancellationToken ct)
    {
        var reason = WoFlow.RequireReason(request.Reason, false, "rilis");
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (wo, poLineIds) = await WoFlow.LoadLockedAsync(db, id, request.RowVersion, lockPoLines: true, ct);
        WoFlow.Require(wo, "Rilis", WoStatus.Draft);

        var evaluation = await WoRelease.EvaluateAsync(db, wo, ct);
        var failed = evaluation.Checks.Where(c => !c.Ok).ToList();
        if (failed.Count > 0)
        {
            var violations = failed.Select(c => new RuleViolation(c.Code, c.Code, null, null, c.Message)).ToList();
            if (failed.Any(c => c.Code.StartsWith("fg.", StringComparison.Ordinal)))
            {
                throw ProblemException.Rule(ProblemCodes.FgNotReady, "FG belum Siap Produksi: " + string.Join(" ", failed.Where(c => c.Code.StartsWith("fg.", StringComparison.Ordinal)).Select(c => c.Message)), violations);
            }

            if (failed.Any(c => c.Code == "po.status"))
            {
                throw ProblemException.StateConflict(failed.First(c => c.Code == "po.status").Message);
            }

            throw ProblemException.Rule(
                failed.Any(c => c.Code == "wo.remaining-plan") ? ProblemCodes.PoRemainingPlanExceeded : ProblemCodes.WoStatusInvalid,
                string.Join(" ", failed.Select(c => c.Message)), violations);
        }

        // Snapshot routing.
        var steps = await db.FgRoutingSteps.AsNoTracking().Where(s => s.FgId == wo.FgId)
            .Join(db.Processes, s => s.ProcessId, p => p.Id, (s, p) => new { s.Seq, p.Id, p.Code, p.Name, p.Type })
            .OrderBy(s => s.Seq).ToListAsync(ct);
        foreach (var step in steps)
        {
            db.WoRoutingSnapshots.Add(new WoRoutingSnapshot
            {
                WoId = wo.Id, Seq = step.Seq, ProcessId = step.Id, ProcessCode = step.Code, ProcessName = step.Name, ProcessType = step.Type,
            });
        }

        // Snapshot tarif: langkah Internal × size yang dipakai WO.
        var poLines = await db.PoLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var sizeIds = poLines.Values.Select(l => l.SizeId).Distinct().ToList();
        var sizeCodes = await db.Sizes.AsNoTracking().Where(s => sizeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code, ct);
        var tariffs = await db.FgTariffs.AsNoTracking().Where(t => t.FgId == wo.FgId && sizeIds.Contains(t.SizeId)).ToListAsync(ct);
        var seqByProcess = steps.ToDictionary(s => s.Id, s => s.Seq);
        foreach (var tariff in tariffs.Where(t => seqByProcess.ContainsKey(t.ProcessId)))
        {
            db.WoTariffSnapshots.Add(new WoTariffSnapshot
            {
                WoId = wo.Id, Seq = seqByProcess[tariff.ProcessId], SizeId = tariff.SizeId, SizeCode = sizeCodes[tariff.SizeId], Rate = tariff.Rate,
            });
        }

        // Label saat rilis.
        var fg = await db.Fgs.AsNoTracking().Where(f => f.Id == wo.FgId).Select(f => new { f.Code, f.Name }).SingleAsync(ct);
        wo.FgCode = fg.Code;
        wo.FgName = fg.Name;
        foreach (var line in wo.Lines)
        {
            var labels = await WoValidation.LabelsAsync(db, poLines[line.PoLineId], ct);
            (line.ColorCode, line.ColorName, line.SizeCode, line.SizeName) = labels;
        }

        wo.ReleasedAt = now;
        wo.ReleasedBy = user.Id;
        WoFlow.Transition(db, wo, WoStatus.Released, reason, now, user.Id);
        await notify.WoReleasedAsync(wo, ct);
        audit.Add(AuditEvents.WoReleased, "WorkOrder", id.ToString(), new { status = "Draft" },
            new { status = "Released", routingSteps = steps.Count, tariffCells = tariffs.Count(t => seqByProcess.ContainsKey(t.ProcessId)) });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>Konveksi menandai WO In Progress sebagai Completed (AC-WO-17/18). Ringkasan progres dibaca klien dari /progress.</summary>
public sealed class CompleteWorkOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries, NotificationEmitter notify)
{
    public async Task<WoDto> HandleAsync(Guid id, WoActionRequest request, CancellationToken ct)
    {
        var reason = WoFlow.RequireReason(request.Reason, false, "penyelesaian");
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (wo, _) = await WoFlow.LoadLockedAsync(db, id, request.RowVersion, lockPoLines: false, ct);
        WoFlow.Require(wo, "Penyelesaian", WoStatus.InProgress);

        wo.CompletedAt = now;
        wo.CompletedBy = user.Id;
        WoFlow.Transition(db, wo, WoStatus.Completed, reason, now, user.Id);
        await notify.WoCompletedAsync(wo, ct);
        audit.Add(AuditEvents.WoCompleted, "WorkOrder", id.ToString(), new { status = "InProgress" }, new { status = "Completed" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>Konveksi mengembalikan WO Completed → In Progress dengan alasan wajib (AC-WO-19).</summary>
public sealed class ResumeWorkOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(Guid id, WoActionRequest request, CancellationToken ct)
    {
        var reason = WoFlow.RequireReason(request.Reason, true, "pembukaan kembali produksi");
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (wo, _) = await WoFlow.LoadLockedAsync(db, id, request.RowVersion, lockPoLines: false, ct);
        WoFlow.Require(wo, "Melanjutkan produksi", WoStatus.Completed);

        WoFlow.Transition(db, wo, WoStatus.InProgress, reason, now, user.Id);
        audit.Add(AuditEvents.WoResumed, "WorkOrder", id.ToString(), new { status = "Completed" }, new { status = "InProgress", reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>Comfi menutup WO Completed (AC-WO-20/21): Dalam Produksi menjadi nol dan Sisa Rencana PO bertambah. Peringatan tidak memblokir.</summary>
public sealed class CloseWorkOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(Guid id, WoActionRequest request, CancellationToken ct)
    {
        var reason = WoFlow.RequireReason(request.Reason, false, "penutupan");
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (wo, poLineIds) = await WoFlow.LoadLockedAsync(db, id, request.RowVersion, lockPoLines: true, ct);
        WoFlow.Require(wo, "Penutupan", WoStatus.Completed);

        var returned = wo.Lines.Sum(l => Math.Max(l.Qty - l.HandedOverQty, 0));
        wo.ClosedAt = now;
        wo.ClosedBy = user.Id;
        WoFlow.Transition(db, wo, WoStatus.Closed, reason, now, user.Id);
        audit.Add(AuditEvents.WoClosed, "WorkOrder", id.ToString(), new { status = "Completed" }, new { status = "Closed", returnedToPoPlan = returned });
        await db.SaveChangesAsync(ct);
        await PoLifecycle.RecomputeInProductionAsync(db, poLineIds, ct);
        await tx.CommitAsync(ct);
        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>Comfi membuka kembali WO Closed → Completed (AC-WO-22). Ditolak bila Sisa Rencana PO tidak lagi mencukupi.</summary>
public sealed class ReopenWorkOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(Guid id, WoActionRequest request, CancellationToken ct)
    {
        var reason = WoFlow.RequireReason(request.Reason, true, "reopen");
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (wo, poLineIds) = await WoFlow.LoadLockedAsync(db, id, request.RowVersion, lockPoLines: true, ct);
        WoFlow.Require(wo, "Reopen", WoStatus.Closed);

        var poStatus = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == wo.PoId).Select(p => p.Status).SingleAsync(ct);
        if (poStatus == PoStatus.Cancelled)
        {
            throw ProblemException.StateConflict("WO tidak dapat dibuka kembali karena PO sudah dibatalkan.");
        }

        var poLines = await db.PoLines.AsNoTracking().Where(l => poLineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var violations = new List<RuleViolation>();
        foreach (var line in wo.Lines)
        {
            var need = Math.Max(line.Qty - line.HandedOverQty, 0);
            var available = poLines[line.PoLineId].RemainingPlanQty; // WO Closed tidak terhitung di Dalam Produksi
            if (need > available)
            {
                var label = $"{line.ColorCode} / {line.SizeCode}";
                var message = $"{label}: butuh {need} untuk dihidupkan kembali, Sisa Rencana PO hanya {available} (selisih {need - available}).";
                violations.Add(new RuleViolation(ProblemCodes.PoRemainingPlanExceeded, label, need, available, message));
            }
        }

        if (violations.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.PoRemainingPlanExceeded,
                "WO tidak dapat dibuka kembali karena Sisa Rencana PO sudah dipakai WO lain. Sesuaikan WO lain atau buat PO baru.", violations);
        }

        WoFlow.Transition(db, wo, WoStatus.Completed, reason, now, user.Id);
        audit.Add(AuditEvents.WoReopened, "WorkOrder", id.ToString(), new { status = "Closed" }, new { status = "Completed", reason });
        await db.SaveChangesAsync(ct);
        await PoLifecycle.RecomputeInProductionAsync(db, poLineIds, ct);
        await tx.CommitAsync(ct);
        return await queries.GetDetailAsync(id, ct);
    }
}

/// <summary>Membatalkan WO Draft/Released tanpa aktivitas (AC-WO-24/25). Alasan wajib; tidak dapat diaktifkan kembali.</summary>
public sealed class CancelWorkOrderHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, WoQueries queries)
{
    public async Task<WoDto> HandleAsync(Guid id, WoActionRequest request, CancellationToken ct)
    {
        var reason = WoFlow.RequireReason(request.Reason, true, "pembatalan")!;
        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var (wo, poLineIds) = await WoFlow.LoadLockedAsync(db, id, request.RowVersion, lockPoLines: true, ct);
        WoFlow.Require(wo, "Pembatalan", WoStatus.Draft, WoStatus.Released);

        var lineIds = wo.Lines.Select(l => l.Id).ToList();
        var hasOutput = await db.OutputEntries.AnyAsync(o => o.WoId == id, ct);
        var hasVendor = await db.VendorMovements.AnyAsync(v => v.WoId == id, ct);
        var hasHandover = wo.Lines.Any(l => l.HandedOverQty > 0) || await db.FgHandoverLines.AnyAsync(h => lineIds.Contains(h.WoLineId), ct);
        if (hasOutput || hasVendor || hasHandover)
        {
            throw ProblemException.Rule(ProblemCodes.WoHasActivity,
                "WO tidak dapat dibatalkan karena sudah ada output, pengiriman vendor, atau serah terima.");
        }

        wo.CancelReason = reason;
        WoFlow.Transition(db, wo, WoStatus.Cancelled, reason, now, user.Id);
        audit.Add(AuditEvents.WoCancelled, "WorkOrder", id.ToString(), new { status = "Draft/Released" }, new { status = "Cancelled", reason });
        await db.SaveChangesAsync(ct);
        await PoLifecycle.RecomputeInProductionAsync(db, poLineIds, ct);
        await tx.CommitAsync(ct);
        return await queries.GetDetailAsync(id, ct);
    }
}
