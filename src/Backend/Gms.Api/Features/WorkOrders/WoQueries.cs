using Gms.Api.Common;
using Gms.Api.Features.Master;
using Gms.Api.Security;
using Gms.Domain.Production;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.WorkOrders;

/// <summary>Pembaca WO: daftar, detail, matriks progres, snapshot tarif, riwayat status, opsi pembuatan, dan pratinjau aksi.</summary>
public sealed class WoQueries(GmsDbContext db, ICurrentUser user, AppTimeZone zone, TimeProvider time)
{
    public static readonly WoStatus[] ActiveStatuses = [WoStatus.Draft, WoStatus.Released, WoStatus.InProgress, WoStatus.Completed];

    // ===== Detail =====

    public async Task<WoDto> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var head = await (
            from w in db.WorkOrders.AsNoTracking()
            where w.Id == id
            join p in db.PurchaseOrders on w.PoId equals p.Id
            select new
            {
                Wo = w, PoCode = p.Code, p.CustomerPoNumber,
                CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First(),
            }).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("WO");

        var lines = await LoadLinesAsync(id, ct);
        var (steps, balances) = await LoadStepsAsync(id, lines.Select(l => l.Dto.Id).ToList(), ct);

        var total = lines.Sum(l => l.Dto.Qty);
        var lastSeq = steps.Count == 0 ? (int?)null : steps.Max(s => s.Seq);
        var lastOutput = lines.ToDictionary(l => l.Dto.Id, l => lastSeq is { } seq ? Balance(balances, l.Dto.Id, seq).Qty : 0);
        var handed = lines.Sum(l => l.Dto.HandedOverQty);
        var unhanded = lines.Sum(l => Math.Max(lastOutput[l.Dto.Id] - l.Dto.HandedOverQty, 0));
        var atVendor = steps.Where(s => s.ProcessType == ProcessType.Vendor)
            .Sum(s => lines.Sum(l => { var b = Balance(balances, l.Dto.Id, s.Seq); return Math.Max(b.SentQty - b.Qty, 0); }));

        return new WoDto(
            head.Wo.Id, head.Wo.Code, head.Wo.PoId, head.PoCode, head.CustomerPoNumber, head.CustomerName,
            head.Wo.FgId, head.Wo.FgCode, head.Wo.FgName, head.Wo.Status, head.Wo.StartDate, head.Wo.TargetDate,
            head.Wo.Notes, head.Wo.CancelReason, lines.Select(l => l.Dto).ToList(), total,
            Percent(lastOutput.Values.Sum(), total), handed, unhanded, atVendor, head.Wo.ReleasedAt,
            AllowedActions(head.Wo.Status), head.Wo.RowVersion);
    }

    internal sealed record LineRow(WoLineDto Dto, PoLine PoLine);

    internal async Task<List<LineRow>> LoadLinesAsync(Guid woId, CancellationToken ct)
    {
        var rows = await (
            from l in db.WoLines.AsNoTracking()
            where l.WoId == woId
            join pl in db.PoLines on l.PoLineId equals pl.Id
            join s in db.Sizes on pl.SizeId equals s.Id
            orderby l.ColorCode, s.SortOrder
            select new { Line = l, PoLine = pl, s.SortOrder }).ToListAsync(ct);

        return rows.Select(r => new LineRow(
            new WoLineDto(
                r.Line.Id, r.Line.PoLineId,
                new ColorSizeRef(r.PoLine.ColorId, r.Line.ColorCode, r.Line.ColorName, r.PoLine.SizeId, r.Line.SizeCode, r.Line.SizeName, r.SortOrder),
                r.Line.Qty, r.Line.HandedOverQty, r.PoLine.RemainingPlanQty),
            r.PoLine)).ToList();
    }

    private async Task<(List<WoRoutingSnapshot> Steps, List<WoStepBalance> Balances)> LoadStepsAsync(
        Guid woId, IReadOnlyCollection<Guid> lineIds, CancellationToken ct)
    {
        var steps = await db.WoRoutingSnapshots.AsNoTracking().Where(s => s.WoId == woId).OrderBy(s => s.Seq).ToListAsync(ct);
        var balances = await db.WoStepBalances.AsNoTracking().Where(b => lineIds.Contains(b.WoLineId)).ToListAsync(ct);
        return (steps, balances);
    }

    private static WoStepBalance Balance(List<WoStepBalance> balances, Guid lineId, int seq) =>
        balances.FirstOrDefault(b => b.WoLineId == lineId && b.Seq == seq) ?? new WoStepBalance { WoLineId = lineId, Seq = seq };

    private static decimal Percent(int output, int target) => target <= 0 ? 0 : Math.Round(output * 100m / target, 1);

    /// <summary>Aksi yang diizinkan = status × permission. Penegakan akhir tetap di endpoint.</summary>
    private List<string> AllowedActions(WoStatus status)
    {
        var actions = new List<string>();
        bool Can(string permission) => user.HasPermission(permission);

        switch (status)
        {
            case WoStatus.Draft:
                if (Can(Permissions.WorkOrder.Edit)) { actions.Add(WoActions.Edit); }
                if (Can(Permissions.WorkOrder.Release)) { actions.Add(WoActions.Release); }
                if (Can(Permissions.WorkOrder.Cancel)) { actions.Add(WoActions.Cancel); }
                break;
            case WoStatus.Released:
                if (Can(Permissions.WorkOrder.Edit)) { actions.Add(WoActions.EditQuantities); }
                if (Can(Permissions.ProductionOutput.Create)) { actions.Add(WoActions.InputOutput); }
                if (Can(Permissions.WorkOrder.Cancel)) { actions.Add(WoActions.Cancel); }
                break;
            case WoStatus.InProgress:
                if (Can(Permissions.WorkOrder.Edit)) { actions.Add(WoActions.EditQuantities); }
                if (Can(Permissions.ProductionOutput.Create)) { actions.Add(WoActions.InputOutput); }
                if (Can(Permissions.SerahTerimaFG.Create)) { actions.Add(WoActions.HandoverFg); }
                if (Can(Permissions.WorkOrder.Complete)) { actions.Add(WoActions.Complete); }
                break;
            case WoStatus.Completed:
                if (Can(Permissions.SerahTerimaFG.Create)) { actions.Add(WoActions.HandoverFg); }
                if (Can(Permissions.WorkOrder.Complete)) { actions.Add(WoActions.Resume); }
                if (Can(Permissions.WorkOrder.Close)) { actions.Add(WoActions.Close); }
                break;
            case WoStatus.Closed:
                if (Can(Permissions.WorkOrder.Reopen)) { actions.Add(WoActions.Reopen); }
                break;
        }

        return actions;
    }

    // ===== Progres =====

    public async Task<WoProgressDto> GetProgressAsync(Guid id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            throw ProblemException.NotFound("WO");
        }

        var lines = await LoadLinesAsync(id, ct);
        var (steps, balances) = await LoadStepsAsync(id, lines.Select(l => l.Dto.Id).ToList(), ct);
        var total = lines.Sum(l => l.Dto.Qty);

        var result = steps.Select(step =>
        {
            var cells = lines.Select(l =>
            {
                var b = Balance(balances, l.Dto.Id, step.Seq);
                return new WoProgressCell(l.Dto.Id, b.Qty, l.Dto.Qty, b.SentQty);
            }).ToList();
            var output = cells.Sum(c => c.Output);
            var sent = cells.Sum(c => c.Sent);
            var state = output >= total && total > 0 ? StepState.Done : output > 0 || sent > 0 ? StepState.InProgress : StepState.NotStarted;
            var vendor = step.ProcessType == ProcessType.Vendor;
            return new WoStepProgress(
                step.Seq, step.ProcessCode, step.ProcessName, step.ProcessType, cells, output, total, Percent(output, total), state,
                vendor ? sent : 0, vendor ? Math.Max(sent - output, 0) : 0);
        }).ToList();

        var lastSeq = steps.Count == 0 ? (int?)null : steps.Max(s => s.Seq);
        var handed = lines.Sum(l => l.Dto.HandedOverQty);
        var unhanded = lines.Sum(l => Math.Max((lastSeq is { } seq ? Balance(balances, l.Dto.Id, seq).Qty : 0) - l.Dto.HandedOverQty, 0));
        return new WoProgressDto(id, lines.Select(l => l.Dto).ToList(), result, handed, unhanded);
    }

    // ===== Snapshot tarif & riwayat =====

    public async Task<WoTariffSnapshotDto> GetTariffSnapshotAsync(Guid id, CancellationToken ct)
    {
        var wo = await db.WorkOrders.AsNoTracking().Where(w => w.Id == id)
            .Select(w => new
            {
                w.ReleasedAt,
                ReleasedByName = db.Users.Where(u => u.Id == w.ReleasedBy).Select(u => u.FullName).FirstOrDefault(),
            }).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("WO");

        var steps = await db.WoRoutingSnapshots.AsNoTracking().Where(s => s.WoId == id).OrderBy(s => s.Seq)
            .Select(s => new WoSnapshotStep(s.Seq, s.ProcessCode, s.ProcessName, s.ProcessType)).ToListAsync(ct);
        var rates = await db.WoTariffSnapshots.AsNoTracking().Where(t => t.WoId == id).OrderBy(t => t.Seq)
            .Select(t => new WoSnapshotRate(t.Seq, t.SizeId, t.Rate)).ToListAsync(ct);
        var sizeIds = rates.Select(r => r.SizeId).Distinct().ToList();
        var sizes = await db.Sizes.AsNoTracking().Where(s => sizeIds.Contains(s.Id)).OrderBy(s => s.SortOrder)
            .Select(s => new SizeRef(s.Id, s.Code, s.Name, s.SortOrder)).ToListAsync(ct);
        return new WoTariffSnapshotDto(wo.ReleasedAt, wo.ReleasedByName, steps, sizes, rates);
    }

    public async Task<IReadOnlyList<WoStatusHistoryItem>> GetStatusHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            throw ProblemException.NotFound("WO");
        }

        return await db.WoStatusHistory.AsNoTracking().Where(h => h.WoId == id)
            .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
            .Select(h => new WoStatusHistoryItem(
                h.FromStatus, h.ToStatus, h.Reason, h.ChangedAt,
                db.Users.Where(u => u.Id == h.ChangedBy).Select(u => u.FullName).FirstOrDefault()))
            .ToListAsync(ct);
    }

    // ===== Opsi pembuatan =====

    public async Task<WoCreationOptionsDto> GetCreationOptionsAsync(Guid poId, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == poId)
            .Select(p => new
            {
                p.Code, p.Status,
                CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First(),
            }).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("PO");
        if (po.Status is not (PoStatus.Confirmed or PoStatus.InProgress))
        {
            throw ProblemException.StateConflict($"WO hanya dapat dibuat dari PO berstatus Confirmed atau In Progress (status saat ini: {po.Status}).");
        }

        var rows = await (
            from l in db.PoLines.AsNoTracking()
            where l.PoId == poId
            join f in db.Fgs on l.FgId equals f.Id
            join c in db.Colors on l.ColorId equals c.Id
            join s in db.Sizes on l.SizeId equals s.Id
            orderby f.Code, c.Code, s.SortOrder
            select new { Line = l, FgCode = f.Code, FgName = f.Name, ColorCode = c.Code, ColorName = c.Name, SizeCode = s.Code, SizeName = s.Name, s.SortOrder })
            .ToListAsync(ct);

        var options = new List<WoCreationFgOption>();
        foreach (var group in rows.GroupBy(r => r.Line.FgId))
        {
            var (isReady, readiness) = await FgReadiness.EvaluateAsync(db, group.Key, ct);
            var first = group.First();
            var cells = group
                .Select(r => new WoCreationCell(
                    r.Line.Id,
                    new ColorSizeRef(r.Line.ColorId, r.ColorCode, r.ColorName, r.Line.SizeId, r.SizeCode, r.SizeName, r.SortOrder),
                    r.Line.RemainingPlanQty))
                .ToList();
            options.Add(new WoCreationFgOption(group.Key, first.FgCode, first.FgName, isReady, readiness, cells));
        }

        return new WoCreationOptionsDto(poId, po.Code, po.CustomerName, options);
    }

    // ===== Pratinjau rilis dan close =====

    public async Task<WoReleasePreviewDto> PreviewReleaseAsync(Guid id, CancellationToken ct)
    {
        var wo = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw ProblemException.NotFound("WO");
        if (wo.Status != WoStatus.Draft)
        {
            throw ProblemException.StateConflict($"Hanya WO Draft yang dapat dirilis (status saat ini: {wo.Status}).");
        }

        var evaluation = await WoRelease.EvaluateAsync(db, wo, ct);
        return new WoReleasePreviewDto(evaluation.Checks, evaluation.Checks.All(c => c.Ok), evaluation.StepCount, evaluation.TariffCellCount);
    }

    public async Task<WoClosePreviewDto> PreviewCloseAsync(Guid id, CancellationToken ct)
    {
        var wo = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw ProblemException.NotFound("WO");
        if (wo.Status != WoStatus.Completed)
        {
            throw ProblemException.StateConflict($"Hanya WO Completed yang dapat ditutup (status saat ini: {wo.Status}).");
        }

        var lines = await LoadLinesAsync(id, ct);
        var (steps, balances) = await LoadStepsAsync(id, lines.Select(l => l.Dto.Id).ToList(), ct);
        var lastSeq = steps.Count == 0 ? (int?)null : steps.Max(s => s.Seq);

        var remaining = lines.Sum(l => Math.Max(l.Dto.Qty - l.Dto.HandedOverQty, 0));
        var unhanded = lines
            .Select(l => new WoUnhandedCell(l.Dto.ColorSize, Math.Max((lastSeq is { } seq ? Balance(balances, l.Dto.Id, seq).Qty : 0) - l.Dto.HandedOverQty, 0)))
            .Where(c => c.Qty > 0).ToList();
        return new WoClosePreviewDto(remaining, remaining, unhanded);
    }

    // ===== Daftar =====

    public async Task<PagedResult<WoListItem>> ListAsync(WoListQuery query, CancellationToken ct)
    {
        var today = zone.Today(time);
        var wos = db.WorkOrders.AsNoTracking().AsQueryable();

        if (query.Status is { } status)
        {
            wos = wos.Where(w => w.Status == status);
        }

        if (query.PoId is { } poId)
        {
            wos = wos.Where(w => w.PoId == poId);
        }

        if (query.FgId is { } fgId)
        {
            wos = wos.Where(w => w.FgId == fgId);
        }

        if (query.CustomerId is { } customerId)
        {
            wos = wos.Where(w => db.PurchaseOrders.Any(p => p.Id == w.PoId && p.CustomerId == customerId));
        }

        if (query.From is { } from)
        {
            wos = wos.Where(w => w.StartDate >= from);
        }

        if (query.To is { } to)
        {
            wos = wos.Where(w => w.StartDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            wos = wos.Where(w => EF.Functions.ILike(w.Code, pattern)
                                 || db.PurchaseOrders.Any(p => p.Id == w.PoId
                                     && (EF.Functions.ILike(p.Code, pattern) || EF.Functions.ILike(p.CustomerPoNumber ?? "", pattern))));
        }

        if (query.Terlambat)
        {
            wos = wos.Where(w => w.TargetDate < today
                                 && (w.Status == WoStatus.Draft || w.Status == WoStatus.Released || w.Status == WoStatus.InProgress));
        }

        if (query.MenungguClose)
        {
            wos = wos.Where(w => w.Status == WoStatus.Completed);
        }

        var ordered = (query.Sort ?? "code:desc").ToLowerInvariant() switch
        {
            "code:asc" => wos.OrderBy(w => w.Code),
            "targetdate:asc" => wos.OrderBy(w => w.TargetDate == null).ThenBy(w => w.TargetDate).ThenBy(w => w.Code),
            _ => wos.OrderByDescending(w => w.Code),
        };

        var projected = ordered.Select(w => new
        {
            w.Id, w.Code, w.PoId, w.FgCode, w.FgName, w.Status, w.StartDate, w.TargetDate,
            PoCode = db.PurchaseOrders.Where(p => p.Id == w.PoId).Select(p => p.Code).First(),
            CustomerName = db.PurchaseOrders.Where(p => p.Id == w.PoId)
                .Join(db.Customers, p => p.CustomerId, c => c.Id, (p, c) => c.Name).First(),
            Total = db.WoLines.Where(l => l.WoId == w.Id).Sum(l => (int?)l.Qty) ?? 0,
            LastOutput = db.WoStepBalances
                .Where(b => db.WoLines.Any(l => l.Id == b.WoLineId && l.WoId == w.Id)
                            && b.Seq == db.WoRoutingSnapshots.Where(r => r.WoId == w.Id).Max(r => (int?)r.Seq))
                .Sum(b => (int?)b.Qty) ?? 0,
        });

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await projected.CountAsync(ct);
        var rows = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(r => new WoListItem(
            r.Id, r.Code, r.PoId, r.PoCode, r.CustomerName, r.FgCode, r.FgName, r.Status, r.Total,
            Percent(r.LastOutput, r.Total), r.StartDate, r.TargetDate,
            r.TargetDate is { } target && target < today && r.Status is WoStatus.Draft or WoStatus.Released or WoStatus.InProgress)).ToList();
        return new PagedResult<WoListItem>(items, page, pageSize, total);
    }
}
