using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Master;
using Gms.Domain.Production;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Production;
using Microsoft.EntityFrameworkCore;
using Gms.Api.Features.Notifications;

namespace Gms.Api.Features.Production;

/// <summary>Batas kirim/terima dan ringkasan per vendor untuk layar vendor (AC-PR-23/24/26). Hasil hitung server; tidak mengunci.</summary>
public sealed class GetVendorContextHandler(GmsDbContext db)
{
    public async Task<VendorContextDto> HandleAsync(Guid woId, int seq, Guid? vendorId, CancellationToken ct)
    {
        var wo = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(w => w.Id == woId, ct) ?? throw ProblemException.NotFound("WO");
        var steps = await db.WoRoutingSnapshots.AsNoTracking().Where(s => s.WoId == woId).OrderBy(s => s.Seq).ToListAsync(ct);
        var index = steps.FindIndex(s => s.Seq == seq);
        if (index < 0)
        {
            throw ProblemException.Validation("seq", "Langkah tidak ada pada routing WO ini.");
        }

        var lines = await db.WoLines.AsNoTracking().Where(l => l.WoId == woId).OrderBy(l => l.ColorCode).ThenBy(l => l.SizeCode).ToListAsync(ct);
        var lineIds = lines.Select(l => l.Id).ToList();
        var balances = await db.WoStepBalances.AsNoTracking().Where(b => lineIds.Contains(b.WoLineId)).ToListAsync(ct);
        Balance Of(Guid lineId, int s) => balances.Where(b => b.WoLineId == lineId && b.Seq == s).Select(b => new Balance(b.Qty, b.SentQty)).FirstOrDefault() ?? new Balance(0, 0);

        var perVendor = await (
            from l in db.VendorMovementLines.AsNoTracking()
            join m in db.VendorMovements.AsNoTracking() on l.MovementId equals m.Id
            where m.WoId == woId && m.Seq == seq && m.Status == DocStatus.Active
            group l by new { l.WoLineId, m.VendorId, m.Kind } into g
            select new { g.Key.WoLineId, g.Key.VendorId, g.Key.Kind, Qty = g.Sum(x => x.Qty) }).ToListAsync(ct);

        var sizes = await db.PoLines.AsNoTracking().Where(p => lines.Select(l => l.PoLineId).Contains(p.Id))
            .Join(db.Sizes, p => p.SizeId, s => s.Id, (p, s) => new { p.Id, p.ColorId, p.SizeId, s.SortOrder }).ToDictionaryAsync(x => x.Id, ct);

        var columns = lines.Select(l =>
        {
            var mine = Of(l.Id, seq);
            var bound = index == 0 ? l.Qty : Of(l.Id, steps[index - 1].Seq).Qty;
            var maxReceive = vendorId is { } v
                ? Math.Max(
                    perVendor.Where(x => x.WoLineId == l.Id && x.VendorId == v && x.Kind == VendorMovementKind.Send).Sum(x => x.Qty)
                    - perVendor.Where(x => x.WoLineId == l.Id && x.VendorId == v && x.Kind == VendorMovementKind.Receive).Sum(x => x.Qty), 0)
                : 0;
            var info = sizes[l.PoLineId];
            return new VendorColumn(l.Id, new ColorSizeRef(info.ColorId, l.ColorCode, l.ColorName, info.SizeId, l.SizeCode, l.SizeName, info.SortOrder),
                Math.Max(bound - mine.Sent, 0), maxReceive, mine.Sent, mine.Qty);
        }).ToList();

        var vendorIds = perVendor.Select(x => x.VendorId).Distinct().ToList();
        var names = await db.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Name, ct);
        var summaries = vendorIds.Select(id =>
        {
            var sent = perVendor.Where(x => x.VendorId == id && x.Kind == VendorMovementKind.Send).Sum(x => x.Qty);
            var received = perVendor.Where(x => x.VendorId == id && x.Kind == VendorMovementKind.Receive).Sum(x => x.Qty);
            return new VendorSummary(id, names[id], sent, received, sent - received);
        }).OrderBy(s => s.VendorName).ToList();

        string? blocked = wo.Status is WoStatus.Released or WoStatus.InProgress ? null : StepLedger.DisabledReasonFor(wo.Status);
        if (blocked is null && steps[index].ProcessType != ProcessType.Vendor)
        {
            blocked = "Langkah ini bertipe Internal; gunakan Input Output.";
        }

        return new VendorContextDto(woId, wo.Code, seq, steps[index].ProcessName, vendorId, columns, summaries, blocked);
    }
}

internal static class VendorRules
{
    public static async Task<Vendor> RequireVendorAsync(GmsDbContext db, Guid vendorId, bool mustBeActive, CancellationToken ct)
    {
        var vendor = await db.Vendors.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vendorId, ct)
                     ?? throw ProblemException.Validation("vendorId", "Vendor tidak ditemukan.");
        if (mustBeActive && !vendor.IsActive)
        {
            throw ProblemException.Validation("vendorId", $"Vendor {vendor.Code} nonaktif.");
        }

        return vendor;
    }

    public static void ValidateDocFields(DateOnly date, DateOnly today, string? docNo, string? notes, ErrorBag errors)
    {
        if (date > today)
        {
            errors.Add("date", "Tanggal tidak boleh di masa depan.");
        }

        if (docNo is { Length: > 100 })
        {
            errors.Add("docNo", "Nomor dokumen maksimal 100 karakter.");
        }

        if (notes is { Length: > 500 })
        {
            errors.Add("notes", "Keterangan maksimal 500 karakter.");
        }
    }

    public static VendorMovement Build(
        WoContext ctx, int seq, Guid vendorId, VendorMovementKind kind, DateOnly date, string? docNo, string? notes,
        List<(WoLine Line, int Qty)> cells, Guid? userId, DateTimeOffset now, Guid? replaces = null)
    {
        var movement = new VendorMovement
        {
            WoId = ctx.Wo.Id, Seq = seq, VendorId = vendorId, Kind = kind, MovementDate = date,
            DocNo = string.IsNullOrWhiteSpace(docNo) ? null : docNo.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            ReplacesId = replaces, CreatedAt = now, CreatedBy = userId,
        };
        foreach (var (line, qty) in cells)
        {
            movement.Lines.Add(new VendorMovementLine { MovementId = movement.Id, WoLineId = line.Id, Qty = qty });
        }

        return movement;
    }

    public static StepInfo RequireVendorStep(WoContext ctx, int seq)
    {
        var step = ctx.Step(seq) ?? throw ProblemException.Validation("seq", "Langkah tidak ada pada routing WO ini.");
        if (step.Type != ProcessType.Vendor)
        {
            throw ProblemException.Validation("seq", "Langkah bertipe Internal dicatat lewat Input Output.");
        }

        return step;
    }
}

/// <summary>Mencatat Kirim atau Terima vendor (AC-PR-22..25). Terima pertama pada WO Released memicu In Progress.</summary>
public sealed class SaveVendorMovementHandler(GmsDbContext db, StepLedger ledger, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task<Guid> HandleAsync(SaveVendorMovementRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        VendorRules.ValidateDocFields(request.Date, ledger.Today, request.DocNo, request.Notes, errors);
        errors.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await ledger.BeginAsync(request.WoId, [], ct); // periode gaji tidak memengaruhi catatan vendor (AC-PR-29)
        StepLedger.RequireOpenForEntries(ctx.Wo);
        VendorRules.RequireVendorStep(ctx, request.Seq);
        await VendorRules.RequireVendorAsync(db, request.VendorId, mustBeActive: true, ct);

        var cells = CellRules.Parse(request.Cells, ctx, allowNegative: false, "cells", errors);
        errors.ThrowIfAny();
        if (cells.Count == 0)
        {
            throw ProblemException.Validation("cells", "Minimal satu sel harus memiliki qty lebih dari 0.");
        }

        var movement = VendorRules.Build(ctx, request.Seq, request.VendorId, request.Kind, request.Date, request.DocNo, request.Notes,
            cells, user.Id, time.GetUtcNow());
        db.VendorMovements.Add(movement);
        await db.SaveChangesAsync(ct);

        var diff = await ledger.RecomputeAsync(ctx, ct);
        StepLedger.ThrowIfAny(await ledger.ValidateAsync(ctx, diff.After, new HashSet<int> { request.Seq }, ct));

        if (request.Kind == VendorMovementKind.Receive)
        {
            await ledger.MarkInProgressAsync(ctx, user.Id, ct);
        }

        audit.Add(AuditEvents.VendorMovementCreated, "VendorMovement", movement.Id.ToString(), null,
            new { request.WoId, request.Seq, request.VendorId, kind = request.Kind.ToString(), request.Date, totalQty = cells.Sum(c => c.Qty) });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return movement.Id;
    }
}

/// <summary>Void Kirim/Terima dengan alasan wajib; kumulatif dan per vendor dicek pada kondisi akhir (AC-PR-27, E6, E7).</summary>
public sealed class VoidVendorMovementHandler(GmsDbContext db, StepLedger ledger, ICurrentUser user, IAuditWriter audit, TimeProvider time, NotificationEmitter notify)
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
        var woId = await db.VendorMovements.AsNoTracking().Where(m => m.Id == id).Select(m => (Guid?)m.WoId).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Catatan vendor");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await ledger.BeginAsync(woId, [], ct);
        StepLedger.RequireOpenForEntries(ctx.Wo);

        var movement = await db.VendorMovements.SingleAsync(m => m.Id == id, ct);
        if (movement.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Catatan sudah di-void dan tidak dapat diaktifkan kembali.");
        }

        movement.Status = DocStatus.Voided;
        movement.VoidReason = reason ?? "(pratinjau)";
        movement.VoidedAt = time.GetUtcNow();
        movement.VoidedBy = user.Id;
        await db.SaveChangesAsync(ct);

        var diff = await ledger.RecomputeAsync(ctx, ct);
        var violations = await ledger.ValidateAsync(ctx, diff.After, new HashSet<int> { movement.Seq }, ct);
        if (dryRun)
        {
            var preview = StepLedger.BuildPreview(ctx, diff, violations, ["Catatan akan di-void dan tidak dihitung pada kumulatif."]);
            await tx.RollbackAsync(ct);
            return preview;
        }

        StepLedger.ThrowIfAny(violations);
        await notify.VendorChangedAsync(movement, "void", ct);
        audit.Add(AuditEvents.VendorMovementVoided, "VendorMovement", id.ToString(), new { status = "Active" },
            new { status = "Voided", reason, kind = movement.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }
}

/// <summary>Koreksi vendor = void + catatan baru atomik (AC-PR-27). Jenis (Kirim/Terima) dan langkah tetap.</summary>
public sealed class CorrectVendorMovementHandler(GmsDbContext db, StepLedger ledger, ICurrentUser user, IAuditWriter audit, TimeProvider time, NotificationEmitter notify)
{
    public async Task<ImpactPreview> PreviewAsync(Guid id, CorrectVendorMovementRequest request, CancellationToken ct) =>
        (await RunAsync(id, request, dryRun: true, ct)).Preview!;

    public async Task<Guid> HandleAsync(Guid id, CorrectVendorMovementRequest request, CancellationToken ct) =>
        (await RunAsync(id, request, dryRun: false, ct)).NewId;

    private async Task<(Guid NewId, ImpactPreview? Preview)> RunAsync(Guid id, CorrectVendorMovementRequest request, bool dryRun, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        var errors = new ErrorBag();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            errors.Add("reason", "Alasan koreksi wajib diisi (maksimal 500 karakter).");
        }

        VendorRules.ValidateDocFields(request.Date, ledger.Today, request.DocNo, request.Notes, errors);
        errors.ThrowIfAny();

        var woId = await db.VendorMovements.AsNoTracking().Where(m => m.Id == id).Select(m => (Guid?)m.WoId).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Catatan vendor");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ctx = await ledger.BeginAsync(woId, [], ct);
        StepLedger.RequireOpenForEntries(ctx.Wo);

        var movement = await db.VendorMovements.SingleAsync(m => m.Id == id, ct);
        if (movement.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Catatan yang sudah di-void tidak dapat dikoreksi.");
        }

        if (request.VendorId != movement.VendorId)
        {
            await VendorRules.RequireVendorAsync(db, request.VendorId, mustBeActive: true, ct);
        }

        var cells = CellRules.Parse(request.Cells, ctx, allowNegative: false, "cells", errors);
        errors.ThrowIfAny();
        if (cells.Count == 0)
        {
            throw ProblemException.Validation("cells", "Minimal satu sel harus memiliki qty lebih dari 0.");
        }

        var now = time.GetUtcNow();
        movement.Status = DocStatus.Voided;
        movement.VoidReason = $"Dikoreksi: {reason}";
        movement.VoidedAt = now;
        movement.VoidedBy = user.Id;

        var replacement = VendorRules.Build(ctx, movement.Seq, request.VendorId, movement.Kind, request.Date, request.DocNo, request.Notes,
            cells, user.Id, now, movement.Id);
        db.VendorMovements.Add(replacement);
        await db.SaveChangesAsync(ct);

        var diff = await ledger.RecomputeAsync(ctx, ct);
        var violations = await ledger.ValidateAsync(ctx, diff.After, new HashSet<int> { movement.Seq }, ct);
        if (dryRun)
        {
            var preview = StepLedger.BuildPreview(ctx, diff, violations, ["Catatan asal di-void dan diganti catatan baru dalam satu transaksi."]);
            await tx.RollbackAsync(ct);
            return (Guid.Empty, preview);
        }

        StepLedger.ThrowIfAny(violations);
        if (movement.Kind == VendorMovementKind.Receive)
        {
            await ledger.MarkInProgressAsync(ctx, user.Id, ct);
        }

        await notify.VendorChangedAsync(movement, "correct", ct);
        audit.Add(AuditEvents.VendorMovementCorrected, "VendorMovement", id.ToString(), new { movement.VendorId },
            new { replacedBy = replacement.Id, request.VendorId, request.Date, reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (replacement.Id, null);
    }
}
