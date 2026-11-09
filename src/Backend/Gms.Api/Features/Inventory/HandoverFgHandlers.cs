using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Features.Production;
using Gms.Api.Security;
using Gms.Domain.Inventory;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Inventory;

internal static class HandoverRules
{
    public static void ValidateHeader(DateOnly date, DateOnly today, string? docNo, string? receivedBy, string? notes, ErrorBag errors)
    {
        if (date > today)
        {
            errors.Add("date", "Tanggal tidak boleh di masa depan.");
        }

        if (docNo is { Length: > 100 })
        {
            errors.Add("docNo", "Nomor dokumen maksimal 100 karakter.");
        }

        if (receivedBy is { Length: > 150 })
        {
            errors.Add("receivedBy", "Diterima Oleh maksimal 150 karakter.");
        }

        if (notes is { Length: > 500 })
        {
            errors.Add("notes", "Catatan maksimal 500 karakter.");
        }
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string RequireReason(string? reason, string aksi)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 500)
        {
            throw ProblemException.Validation("reason", $"Alasan {aksi} wajib diisi (maksimal 500 karakter).");
        }

        return text;
    }
}

/// <summary>Batas serah terima per sel untuk layar input (AC-ST-01). Hasil hitung server; tidak mengunci.</summary>
public sealed class GetHandoverFgContextHandler(GmsDbContext db)
{
    public async Task<HandoverFgContextDto> HandleAsync(Guid woId, CancellationToken ct)
    {
        var wo = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(w => w.Id == woId, ct) ?? throw ProblemException.NotFound("WO");
        var poCode = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == wo.PoId).Select(p => p.Code).SingleAsync(ct);
        var lastSeq = await db.WoRoutingSnapshots.Where(r => r.WoId == woId).MaxAsync(r => (int?)r.Seq, ct);

        var rows = await (
            from l in db.WoLines.AsNoTracking()
            where l.WoId == woId
            join p in db.PoLines on l.PoLineId equals p.Id
            join s in db.Sizes on p.SizeId equals s.Id
            orderby l.ColorCode, s.SortOrder
            select new { Line = l, p.ColorId, p.SizeId, s.SortOrder }).ToListAsync(ct);
        var lineIds = rows.Select(r => r.Line.Id).ToList();
        var last = lastSeq is { } seq
            ? await db.WoStepBalances.AsNoTracking().Where(b => lineIds.Contains(b.WoLineId) && b.Seq == seq).ToDictionaryAsync(b => b.WoLineId, b => b.Qty, ct)
            : [];

        var columns = rows.Select(r =>
        {
            var output = last.GetValueOrDefault(r.Line.Id);
            return new HandoverColumn(
                r.Line.Id, new ColorSizeRef(r.ColorId, r.Line.ColorCode, r.Line.ColorName, r.SizeId, r.Line.SizeCode, r.Line.SizeName, r.SortOrder),
                Math.Max(output - r.Line.HandedOverQty, 0), r.Line.HandedOverQty, output, r.Line.Qty);
        }).ToList();

        string? blocked = null;
        try
        {
            HandoverEngine.RequireHandoverStatus(wo);
        }
        catch (ProblemException ex)
        {
            blocked = ex.Detail;
        }

        return new HandoverFgContextDto(woId, wo.Code, poCode, wo.FgCode, wo.FgName, columns, blocked);
    }
}

/// <summary>Satu inti untuk pratinjau dan simpan: pratinjau = jalur yang sama lalu rollback.</summary>
public sealed class CreateHandoverFgHandler(
    GmsDbContext db, HandoverEngine engine, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task<HandoverFgPreviewDto> PreviewAsync(SaveHandoverFgRequest request, CancellationToken ct) =>
        (await RunAsync(request, dryRun: true, ct)).Preview!;

    public async Task<HandoverFgResultDto> HandleAsync(SaveHandoverFgRequest request, CancellationToken ct) =>
        (await RunAsync(request, dryRun: false, ct)).Result!;

    private async Task<(HandoverFgResultDto? Result, HandoverFgPreviewDto? Preview)> RunAsync(
        SaveHandoverFgRequest request, bool dryRun, CancellationToken ct)
    {
        var errors = new ErrorBag();
        HandoverRules.ValidateHeader(request.Date, engine.Today, request.DocNo, request.ReceivedBy, request.Notes, errors);
        errors.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var state = await engine.OpenAsync(request.WoId, ct);

        var cells = CellRules.Parse(request.Cells, state.Ctx, allowNegative: false, "cells", errors);
        errors.ThrowIfAny();
        if (cells.Count == 0)
        {
            throw ProblemException.Validation("cells", "Minimal satu sel harus memiliki qty lebih dari 0.");
        }

        var now = time.GetUtcNow();
        var doc = new FgHandover
        {
            WoId = request.WoId, HandoverDate = request.Date, DocNo = HandoverRules.Clean(request.DocNo),
            ReceivedBy = HandoverRules.Clean(request.ReceivedBy), Notes = HandoverRules.Clean(request.Notes),
            CreatedAt = now, CreatedBy = user.Id,
        };
        var estimates = engine.Apply(state, doc, cells, user.Id, now);
        HandoverEngine.ThrowIfAny(engine.CheckFinalState(state));
        db.FgHandovers.Add(doc);

        var auto = estimates.Sum(e => e.Auto);
        var free = estimates.Sum(e => e.Free);
        if (dryRun)
        {
            await tx.RollbackAsync(ct);
            return (null, new HandoverFgPreviewDto(auto, free, state.PoCode, estimates.Select(e => new HandoverCellEstimate(e.WoLineId, e.Auto, e.Free)).ToList()));
        }

        audit.Add(AuditEvents.HandoverFgCreated, "FgHandover", doc.Id.ToString(), null,
            new { request.WoId, request.Date, totalQty = auto + free, autoAllocated = auto, freeStock = free });
        await engine.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return (new HandoverFgResultDto(doc.Id, auto, free, state.PoCode), null);
    }
}

/// <summary>Void: alokasi otomatis dilepas, stok dikurangi, kondisi akhir diperiksa (AC-ST-07/08/10/11).</summary>
public sealed class VoidHandoverFgHandler(GmsDbContext db, HandoverEngine engine, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task<ImpactPreview> PreviewAsync(Guid id, CancellationToken ct) => (await RunAsync(id, null, dryRun: true, ct))!;

    public async Task HandleAsync(Guid id, ReasonRequest request, CancellationToken ct) =>
        await RunAsync(id, HandoverRules.RequireReason(request.Reason, "void"), dryRun: false, ct);

    private async Task<ImpactPreview?> RunAsync(Guid id, string? reason, bool dryRun, CancellationToken ct)
    {
        var woId = await db.FgHandovers.AsNoTracking().Where(h => h.Id == id).Select(h => (Guid?)h.WoId).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Serah terima");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var state = await engine.OpenAsync(woId, ct);
        var doc = await db.FgHandovers.Include(h => h.Lines).SingleAsync(h => h.Id == id, ct);
        if (doc.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Dokumen sudah di-void dan tidak dapat diaktifkan kembali.");
        }

        engine.Reverse(state, doc, reason ?? "(pratinjau)", user.Id, time.GetUtcNow());
        var violations = engine.CheckFinalState(state);
        if (dryRun)
        {
            var preview = HandoverEngine.BuildImpact(state, violations, ["Alokasi otomatis yang berasal dari dokumen ini dilepas lebih dulu, lalu stok dikurangi."]);
            await tx.RollbackAsync(ct);
            return preview;
        }

        HandoverEngine.ThrowIfAny(violations);
        audit.Add(AuditEvents.HandoverFgVoided, "FgHandover", id.ToString(), new { status = "Active" }, new { status = "Voided", reason });
        await engine.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }
}

/// <summary>Koreksi = void dokumen lama + dokumen baru dalam satu transaksi; pemeriksaan hanya pada kondisi akhir (AC-ST-09).</summary>
public sealed class CorrectHandoverFgHandler(GmsDbContext db, HandoverEngine engine, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task<ImpactPreview> PreviewAsync(Guid id, CorrectHandoverFgRequest request, CancellationToken ct) =>
        (await RunAsync(id, request, dryRun: true, ct)).Preview!;

    public async Task<HandoverFgResultDto> HandleAsync(Guid id, CorrectHandoverFgRequest request, CancellationToken ct) =>
        (await RunAsync(id, request, dryRun: false, ct)).Result!;

    private async Task<(HandoverFgResultDto? Result, ImpactPreview? Preview)> RunAsync(
        Guid id, CorrectHandoverFgRequest request, bool dryRun, CancellationToken ct)
    {
        var reason = HandoverRules.RequireReason(request.Reason, "koreksi");
        var errors = new ErrorBag();
        HandoverRules.ValidateHeader(request.Date, engine.Today, request.DocNo, request.ReceivedBy, request.Notes, errors);
        errors.ThrowIfAny();

        var woId = await db.FgHandovers.AsNoTracking().Where(h => h.Id == id).Select(h => (Guid?)h.WoId).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Serah terima");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var state = await engine.OpenAsync(woId, ct);
        var old = await db.FgHandovers.Include(h => h.Lines).SingleAsync(h => h.Id == id, ct);
        if (old.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Dokumen yang sudah di-void tidak dapat dikoreksi.");
        }

        var cells = CellRules.Parse(request.Cells, state.Ctx, allowNegative: false, "cells", errors);
        errors.ThrowIfAny();
        if (cells.Count == 0)
        {
            throw ProblemException.Validation("cells", "Minimal satu sel harus memiliki qty lebih dari 0.");
        }

        var now = time.GetUtcNow();
        engine.Reverse(state, old, $"Dikoreksi: {reason}", user.Id, now);
        var replacement = new FgHandover
        {
            WoId = woId, HandoverDate = request.Date, DocNo = HandoverRules.Clean(request.DocNo),
            ReceivedBy = HandoverRules.Clean(request.ReceivedBy), Notes = HandoverRules.Clean(request.Notes),
            ReplacesId = old.Id, CreatedAt = now, CreatedBy = user.Id,
        };
        var estimates = engine.Apply(state, replacement, cells, user.Id, now);
        var violations = engine.CheckFinalState(state);
        if (dryRun)
        {
            var preview = HandoverEngine.BuildImpact(state, violations, ["Dokumen lama di-void dan diganti dokumen baru dalam satu transaksi."]);
            await tx.RollbackAsync(ct);
            return (null, preview);
        }

        HandoverEngine.ThrowIfAny(violations);
        db.FgHandovers.Add(replacement);
        audit.Add(AuditEvents.HandoverFgCorrected, "FgHandover", id.ToString(), new { totalQty = old.Lines.Sum(l => l.Qty) },
            new { replacedBy = replacement.Id, totalQty = cells.Sum(c => c.Qty), reason });
        await engine.SaveAsync(ct);
        await tx.CommitAsync(ct);
        return (new HandoverFgResultDto(replacement.Id, estimates.Sum(e => e.Auto), estimates.Sum(e => e.Free), state.PoCode), null);
    }
}

/// <summary>Daftar dan detail serah terima FG (AC-ST-28).</summary>
public sealed class HandoverFgQueries(GmsDbContext db, ICurrentUser user)
{
    public async Task<PagedResult<HandoverFgListItem>> ListAsync(HandoverFgListQuery query, CancellationToken ct)
    {
        var rows = Base();
        if (query.WoId is { } woId) { rows = rows.Where(r => r.WoId == woId); }
        if (query.PoId is { } poId) { rows = rows.Where(r => r.PoId == poId); }
        if (query.FgId is { } fgId) { rows = rows.Where(r => r.FgId == fgId); }
        if (query.From is { } from) { rows = rows.Where(r => r.Date >= from); }
        if (query.To is { } to) { rows = rows.Where(r => r.Date <= to); }
        if (query.Status is { } status) { rows = rows.Where(r => r.Status == status); }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            rows = rows.Where(r => EF.Functions.ILike(r.WoCode, pattern) || EF.Functions.ILike(r.PoCode, pattern) || EF.Functions.ILike(r.DocNo ?? "", pattern));
        }

        var ordered = rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await ordered.CountAsync(ct);
        var pageRows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<HandoverFgListItem>(pageRows.Select(ToItem).ToList(), page, pageSize, total);
    }

    public async Task<HandoverFgDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Base().Where(r => r.Id == id).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("Serah terima");
        var doc = await db.FgHandovers.AsNoTracking().SingleAsync(h => h.Id == id, ct);
        var lines = await (
            from l in db.FgHandoverLines.AsNoTracking()
            where l.HandoverId == id
            join wl in db.WoLines on l.WoLineId equals wl.Id
            join p in db.PoLines on wl.PoLineId equals p.Id
            join s in db.Sizes on p.SizeId equals s.Id
            orderby wl.ColorCode, s.SortOrder
            select new HandoverFgLineDto(l.WoLineId,
                new ColorSizeRef(p.ColorId, wl.ColorCode, wl.ColorName, p.SizeId, wl.SizeCode, wl.SizeName, s.SortOrder), l.Qty, l.AutoAllocatedQty))
            .ToListAsync(ct);
        var replacedBy = await db.FgHandovers.AsNoTracking().Where(h => h.ReplacesId == id).Select(h => (Guid?)h.Id).FirstOrDefaultAsync(ct);

        string? disabled = null;
        if (row.Status == DocStatus.Voided)
        {
            disabled = "Dokumen sudah di-void.";
        }
        else if (row.WoStatus is not (WoStatus.InProgress or WoStatus.Completed))
        {
            disabled = row.WoStatus == WoStatus.Closed ? "WO sudah ditutup (Reopen diperlukan)." : "WO tidak dalam status yang mengizinkan.";
        }

        var allowed = disabled is null;
        return new HandoverFgDto(
            ToItem(row), lines, doc.ReceivedBy, doc.Notes, doc.VoidReason, doc.ReplacesId, replacedBy,
            allowed && user.HasPermission(Permissions.SerahTerimaFG.Void),
            allowed && user.HasPermission(Permissions.SerahTerimaFG.Correct), disabled);
    }

    private sealed class Row
    {
        public Guid Id { get; set; }
        public DateOnly Date { get; set; }
        public string? DocNo { get; set; }
        public Guid WoId { get; set; }
        public string WoCode { get; set; } = string.Empty;
        public WoStatus WoStatus { get; set; }
        public Guid PoId { get; set; }
        public string PoCode { get; set; } = string.Empty;
        public Guid FgId { get; set; }
        public string FgCode { get; set; } = string.Empty;
        public int TotalQty { get; set; }
        public DocStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
    }

    private IQueryable<Row> Base() =>
        from h in db.FgHandovers.AsNoTracking()
        join w in db.WorkOrders.AsNoTracking() on h.WoId equals w.Id
        join p in db.PurchaseOrders.AsNoTracking() on w.PoId equals p.Id
        select new Row
        {
            Id = h.Id, Date = h.HandoverDate, DocNo = h.DocNo, WoId = w.Id, WoCode = w.Code, WoStatus = w.Status,
            PoId = p.Id, PoCode = p.Code, FgId = w.FgId, FgCode = w.FgCode,
            TotalQty = db.FgHandoverLines.Where(l => l.HandoverId == h.Id).Sum(l => (int?)l.Qty) ?? 0,
            Status = h.Status, CreatedAt = h.CreatedAt,
            CreatedByName = db.Users.Where(u => u.Id == h.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
        };

    private static HandoverFgListItem ToItem(Row r) =>
        new(r.Id, r.Date, r.DocNo, r.WoId, r.WoCode, r.PoCode, r.FgCode, r.TotalQty, r.Status, r.CreatedByName);
}
