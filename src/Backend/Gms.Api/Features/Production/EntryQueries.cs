using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Production;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Production;

/// <summary>Riwayat gabungan Output, Penyesuaian, Kirim, dan Terima (AC-PR-30..32). Tidak memuat tarif atau nilai rupiah (AC-PR-08).</summary>
public sealed class EntryQueries(GmsDbContext db, ICurrentUser user, StepLedger ledger)
{
    private sealed class Row
    {
        public Guid Id { get; set; }
        public EntryKind Kind { get; set; }
        public Guid WoId { get; set; }
        public string WoCode { get; set; } = string.Empty;
        public WoStatus WoStatus { get; set; }
        public int Seq { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public Guid? WorkerId { get; set; }
        public string? WorkerName { get; set; }
        public Guid? VendorId { get; set; }
        public string? VendorName { get; set; }
        public DateOnly WorkDate { get; set; }
        public int TotalQty { get; set; }
        public DocStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public string? VoidReason { get; set; }
        public Guid? ReplacesId { get; set; }
        public Guid? ReplacedById { get; set; }
    }

    private bool CanSeeOutputs => user.HasPermission(Permissions.ProductionOutput.View);

    private bool CanSeeVendor => user.HasPermission(Permissions.VendorJob.View);

    private IQueryable<Row> OutputRows() =>
        from e in db.OutputEntries.AsNoTracking()
        join w in db.WorkOrders.AsNoTracking() on e.WoId equals w.Id
        select new Row
        {
            Id = e.Id,
            Kind = e.Kind == OutputKind.Output ? EntryKind.Output : EntryKind.Adjustment,
            WoId = e.WoId, WoCode = w.Code, WoStatus = w.Status, Seq = e.Seq,
            ProcessName = db.WoRoutingSnapshots.Where(r => r.WoId == e.WoId && r.Seq == e.Seq).Select(r => r.ProcessName).First(),
            WorkerId = e.WorkerId,
            WorkerName = db.Workers.Where(x => x.Id == e.WorkerId).Select(x => x.Name).First(),
            VendorId = null, VendorName = null,
            WorkDate = e.WorkDate,
            TotalQty = db.OutputEntryLines.Where(l => l.EntryId == e.Id).Sum(l => (int?)l.Qty) ?? 0,
            Status = e.Status, CreatedAt = e.CreatedAt, CreatedBy = e.CreatedBy, VoidReason = e.VoidReason,
            ReplacesId = e.ReplacesEntryId,
            ReplacedById = db.OutputEntries.Where(x => x.ReplacesEntryId == e.Id).Select(x => (Guid?)x.Id).FirstOrDefault(),
        };

    private IQueryable<Row> VendorRows() =>
        from m in db.VendorMovements.AsNoTracking()
        join w in db.WorkOrders.AsNoTracking() on m.WoId equals w.Id
        select new Row
        {
            Id = m.Id,
            Kind = m.Kind == VendorMovementKind.Send ? EntryKind.VendorSend : EntryKind.VendorReceive,
            WoId = m.WoId, WoCode = w.Code, WoStatus = w.Status, Seq = m.Seq,
            ProcessName = db.WoRoutingSnapshots.Where(r => r.WoId == m.WoId && r.Seq == m.Seq).Select(r => r.ProcessName).First(),
            WorkerId = null, WorkerName = null,
            VendorId = m.VendorId,
            VendorName = db.Vendors.Where(x => x.Id == m.VendorId).Select(x => x.Name).First(),
            WorkDate = m.MovementDate,
            TotalQty = db.VendorMovementLines.Where(l => l.MovementId == m.Id).Sum(l => (int?)l.Qty) ?? 0,
            Status = m.Status, CreatedAt = m.CreatedAt, CreatedBy = m.CreatedBy, VoidReason = m.VoidReason,
            ReplacesId = m.ReplacesId,
            ReplacedById = db.VendorMovements.Where(x => x.ReplacesId == m.Id).Select(x => (Guid?)x.Id).FirstOrDefault(),
        };

    private IQueryable<Row> Visible()
    {
        if (!CanSeeOutputs && !CanSeeVendor)
        {
            throw ProblemException.Forbidden();
        }

        if (CanSeeOutputs && CanSeeVendor)
        {
            return OutputRows().Concat(VendorRows());
        }

        return CanSeeOutputs ? OutputRows() : VendorRows();
    }

    public async Task<PagedResult<EntryListItem>> ListAsync(EntryListQuery query, CancellationToken ct)
    {
        var rows = Visible();

        if (query.WoId is { } woId)
        {
            rows = rows.Where(r => r.WoId == woId);
        }

        if (query.Seq is { } seq)
        {
            rows = rows.Where(r => r.Seq == seq);
        }

        if (query.WorkerId is { } workerId)
        {
            rows = rows.Where(r => r.WorkerId == workerId);
        }

        if (query.VendorId is { } vendorId)
        {
            rows = rows.Where(r => r.VendorId == vendorId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(r => r.WorkDate >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(r => r.WorkDate <= to);
        }

        if (query.Kind is { } kind)
        {
            rows = rows.Where(r => r.Kind == kind);
        }

        if (query.HariIni)
        {
            var today = ledger.Today;
            rows = rows.Where(r => r.WorkDate == today);
        }

        var statusFilter = query.Status ?? (query.IncludeVoided ? null : DocStatus.Active);
        if (statusFilter is { } status)
        {
            rows = rows.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            rows = rows.Where(r => EF.Functions.ILike(r.WoCode, pattern) || EF.Functions.ILike(r.WorkerName ?? "", pattern)
                                                                           || EF.Functions.ILike(r.VendorName ?? "", pattern));
        }

        var ordered = rows.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await ordered.CountAsync(ct);
        var pageRows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<EntryListItem>(await ToItemsAsync(pageRows, ct), page, pageSize, total);
    }

    public async Task<EntryDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Visible().Where(r => r.Id == id).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("Entri");
        var item = (await ToItemsAsync([row], ct))[0];

        if (row.Kind is EntryKind.Output or EntryKind.Adjustment)
        {
            var entry = await db.OutputEntries.AsNoTracking().SingleAsync(e => e.Id == id, ct);
            var lines = await LinesAsync(
                db.OutputEntryLines.AsNoTracking().Where(l => l.EntryId == id).Select(l => new { l.WoLineId, l.Qty }).ToListAsync(ct), ct);
            return new EntryDetailDto(item, lines, entry.Reason, entry.OriginalDate, entry.RefEntryId, null, null);
        }

        var movement = await db.VendorMovements.AsNoTracking().SingleAsync(m => m.Id == id, ct);
        var vendorLines = await LinesAsync(
            db.VendorMovementLines.AsNoTracking().Where(l => l.MovementId == id).Select(l => new { l.WoLineId, l.Qty }).ToListAsync(ct), ct);
        return new EntryDetailDto(item, vendorLines, null, null, null, movement.DocNo, movement.Notes);
    }

    private async Task<List<EntryLineDto>> LinesAsync<T>(Task<List<T>> source, CancellationToken ct)
        where T : class
    {
        var raw = (await source).Select(x => (LineId: (Guid)x.GetType().GetProperty("WoLineId")!.GetValue(x)!, Qty: (int)x.GetType().GetProperty("Qty")!.GetValue(x)!)).ToList();
        var ids = raw.Select(r => r.LineId).ToList();
        var info = await (
            from l in db.WoLines.AsNoTracking()
            where ids.Contains(l.Id)
            join p in db.PoLines on l.PoLineId equals p.Id
            join s in db.Sizes on p.SizeId equals s.Id
            select new { l.Id, p.ColorId, p.SizeId, l.ColorCode, l.ColorName, l.SizeCode, l.SizeName, s.SortOrder }).ToDictionaryAsync(x => x.Id, ct);
        return raw.Select(r =>
        {
            var i = info[r.LineId];
            return new EntryLineDto(r.LineId, new ColorSizeRef(i.ColorId, i.ColorCode, i.ColorName, i.SizeId, i.SizeCode, i.SizeName, i.SortOrder), r.Qty);
        }).OrderBy(l => l.ColorSize.ColorCode).ThenBy(l => l.ColorSize.SizeSortOrder).ToList();
    }

    private async Task<List<EntryListItem>> ToItemsAsync(List<Row> rows, CancellationToken ct)
    {
        var closed = await db.PayrollPeriods.AsNoTracking().Where(p => p.Status == PayrollStatus.Closed)
            .Select(p => new { p.StartDate, p.EndDate }).ToListAsync(ct);
        var creatorIds = rows.Select(r => r.CreatedBy).Where(x => x != null).Distinct().ToList();
        var creators = await db.Users.AsNoTracking().Where(u => creatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return rows.Select(r =>
        {
            var isOutput = r.Kind is EntryKind.Output or EntryKind.Adjustment;
            var woOpen = r.WoStatus is WoStatus.Released or WoStatus.InProgress;
            var periodClosed = isOutput && closed.Any(p => p.StartDate <= r.WorkDate && p.EndDate >= r.WorkDate);
            var active = r.Status == DocStatus.Active;

            string? disabled = null;
            if (!active)
            {
                disabled = "Entri sudah di-void.";
            }
            else if (!woOpen)
            {
                disabled = StepLedger.DisabledReasonFor(r.WoStatus);
            }
            else if (periodClosed)
            {
                disabled = "Periode gaji sudah ditutup (gunakan Penyesuaian).";
            }

            var allowed = disabled is null;
            var canVoid = allowed && user.HasPermission(isOutput ? Permissions.ProductionOutput.Void : Permissions.VendorJob.Void);
            var canCorrect = allowed && (isOutput
                ? r.Kind == EntryKind.Output && user.HasPermission(Permissions.ProductionOutput.Correct)
                : user.HasPermission(Permissions.VendorJob.Create) && user.HasPermission(Permissions.VendorJob.Void));

            return new EntryListItem(
                r.Id, r.Kind, r.WoId, r.WoCode, r.Seq, r.ProcessName, r.WorkerId, r.WorkerName, r.VendorId, r.VendorName, r.WorkDate,
                r.TotalQty, r.Status, r.CreatedAt, r.CreatedBy is { } c ? creators.GetValueOrDefault(c) : null, r.VoidReason,
                r.ReplacesId, r.ReplacedById, canVoid, canCorrect, disabled);
        }).ToList();
    }
}
