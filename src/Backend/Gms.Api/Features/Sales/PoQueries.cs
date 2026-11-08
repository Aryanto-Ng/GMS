using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Sales;

/// <summary>Pembaca PO: detail dengan metrik pemenuhan dan daftar dengan filter cepat (juga dipakai Beranda).</summary>
public sealed class PoQueries(GmsDbContext db, ICurrentUser user, AppTimeZone zone, TimeProvider time)
{
    // ===== Detail =====

    public async Task<PoDto> GetDetailAsync(Guid id, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Code, p.CustomerPoNumber, p.CustomerId,
                CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First(),
                p.PoDate, p.Status, p.Notes, p.CancelReason, p.CreatedAt,
                CreatedByName = db.Users.Where(u => u.Id == p.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
                p.RowVersion,
            })
            .SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("PO");

        var today = zone.Today(time);
        var rows = await (
            from l in db.PoLines.AsNoTracking()
            where l.PoId == id
            join f in db.Fgs on l.FgId equals f.Id
            join c in db.Colors on l.ColorId equals c.Id
            join s in db.Sizes on l.SizeId equals s.Id
            orderby f.Code, c.Code, s.SortOrder, l.DeliveryDate
            select new
            {
                Line = l, FgCode = f.Code, FgName = f.Name, ColorCode = c.Code, ColorName = c.Name,
                SizeCode = s.Code, SizeName = s.Name, SizeSort = s.SortOrder,
                HasHistory = db.PoAllocations.Any(a => a.PoLineId == l.Id)
                             || db.WoLines.Any(w => w.PoLineId == l.Id)
                             || db.DeliveryLines.Any(d => d.PoLineId == l.Id),
            }).ToListAsync(ct);

        var activeStatus = po.Status is PoStatus.Confirmed or PoStatus.InProgress;
        var lines = rows.Select(r => new PoLineDto(
            r.Line.Id, r.Line.FgId, r.FgCode, r.FgName,
            new ColorSizeRef(r.Line.ColorId, r.ColorCode, r.ColorName, r.Line.SizeId, r.SizeCode, r.SizeName, r.SizeSort),
            r.Line.DeliveryDate, r.Line.Qty, r.Line.DeliveredQty, r.Line.AllocatedQty, r.Line.InProductionQty,
            r.Line.RemainingPlanQty,
            r.Line.DeliveredQty + r.Line.AllocatedQty + r.Line.InProductionQty,
            r.HasHistory || r.Line.DeliveredQty + r.Line.AllocatedQty + r.Line.InProductionQty > 0,
            activeStatus && r.Line.DeliveryDate < today && r.Line.DeliveredQty < r.Line.Qty,
            r.Line.Notes)).ToList();

        var actions = await AllowedActionsAsync(po.Id, po.Status, lines, ct);
        return new PoDto(
            po.Id, po.Code, po.CustomerPoNumber, po.CustomerId, po.CustomerName, po.PoDate, po.Status, po.Notes, po.CancelReason,
            lines, lines.Sum(l => l.Qty), lines.Sum(l => l.DeliveredQty), lines.Sum(l => l.AllocatedQty),
            lines.Sum(l => l.InProductionQty), lines.Sum(l => l.RemainingPlanQty),
            actions, po.CreatedAt, po.CreatedByName, po.RowVersion);
    }

    /// <summary>Gabungan status + permission (FSD: penegakan sisi UI dari server; penegakan akhir tetap di endpoint).</summary>
    private async Task<IReadOnlyList<string>> AllowedActionsAsync(Guid poId, PoStatus status, List<PoLineDto> lines, CancellationToken ct)
    {
        var actions = new List<string>();
        if (status is PoStatus.Completed or PoStatus.Cancelled)
        {
            return actions;
        }

        if (user.HasPermission(Permissions.CustomerPO.Edit))
        {
            actions.Add(PoActions.Edit);
            actions.Add(PoActions.SetCustomerPoNumber);
        }

        if (status == PoStatus.Draft)
        {
            if (user.HasPermission(Permissions.CustomerPO.Confirm) && lines.Count > 0)
            {
                actions.Add(PoActions.Confirm);
            }
        }
        else
        {
            if (user.HasPermission(Permissions.CustomerPO.Alokasi.Allocate))
            {
                actions.Add(PoActions.Allocate);
            }

            if (user.HasPermission(Permissions.WorkOrder.Create) && lines.Any(l => l.RemainingPlanQty > 0))
            {
                actions.Add(PoActions.CreateWorkOrder);
            }

            if (user.HasPermission(Permissions.Pengiriman.Create) && lines.Any(l => l.AllocatedQty > 0))
            {
                actions.Add(PoActions.CreateDelivery);
            }
        }

        if (user.HasPermission(Permissions.CustomerPO.Cancel) && (await CancelBlockersAsync(poId, ct)).Count == 0)
        {
            actions.Add(PoActions.Cancel);
        }

        return actions;
    }

    /// <summary>Penahan pembatalan PO (AC-PO-26): sudah ada pengiriman, atau WO yang belum ditutup/dibatalkan.</summary>
    public async Task<List<string>> CancelBlockersAsync(Guid poId, CancellationToken ct)
    {
        var blockers = new List<string>();

        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => d.PoId == poId && d.Status == DocStatus.Active)
            .Select(d => "Pengiriman " + d.Code).Take(10).ToListAsync(ct);
        blockers.AddRange(deliveries);

        var deliveredLines = await db.PoLines.AnyAsync(l => l.PoId == poId && l.DeliveredQty > 0, ct);
        if (deliveredLines && deliveries.Count == 0)
        {
            blockers.Add("Sudah ada qty terkirim");
        }

        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(w => w.PoId == poId && w.Status != WoStatus.Closed && w.Status != WoStatus.Cancelled)
            .Select(w => "WO " + w.Code).Take(10).ToListAsync(ct);
        blockers.AddRange(workOrders);
        return blockers;
    }

    // ===== Daftar =====

    public async Task<PagedResult<PoListItem>> ListAsync(PoListQuery query, CancellationToken ct)
    {
        var today = zone.Today(time);
        var weekAhead = today.AddDays(7);
        var pos = db.PurchaseOrders.AsNoTracking().AsQueryable();

        if (query.Status is { } status)
        {
            pos = pos.Where(p => p.Status == status);
        }

        if (query.CustomerId is { } customerId)
        {
            pos = pos.Where(p => p.CustomerId == customerId);
        }

        if (query.From is { } from)
        {
            pos = pos.Where(p => p.PoDate >= from);
        }

        if (query.To is { } to)
        {
            pos = pos.Where(p => p.PoDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            pos = pos.Where(p => EF.Functions.ILike(p.Code, pattern)
                                 || EF.Functions.ILike(p.CustomerPoNumber ?? "", pattern)
                                 || db.Customers.Any(c => c.Id == p.CustomerId && EF.Functions.ILike(c.Name, pattern)));
        }

        if (query.Terlambat)
        {
            pos = pos.Where(p => (p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress)
                                 && db.PoLines.Any(l => l.PoId == p.Id && l.DeliveryDate < today && l.DeliveredQty < l.Qty));
        }

        if (query.JatuhTempo7)
        {
            pos = pos.Where(p => (p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress)
                                 && db.PoLines.Any(l => l.PoId == p.Id && l.DeliveryDate >= today && l.DeliveryDate <= weekAhead
                                                        && l.DeliveredQty < l.Qty));
        }

        if (query.PoCustomerKosong)
        {
            pos = pos.Where(p => p.CustomerPoNumber == null
                                 && (p.Status == PoStatus.Draft || p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress));
        }

        if (query.SisaRencana)
        {
            pos = pos.Where(p => (p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress)
                                 && db.PoLines.Any(l => l.PoId == p.Id && l.Qty - l.DeliveredQty - l.AllocatedQty - l.InProductionQty > 0));
        }

        var ordered = (query.Sort ?? "poDate:desc").ToLowerInvariant() switch
        {
            "code:asc" => pos.OrderBy(p => p.Code),
            "code:desc" => pos.OrderByDescending(p => p.Code),
            "podate:asc" => pos.OrderBy(p => p.PoDate).ThenBy(p => p.Code),
            _ => pos.OrderByDescending(p => p.PoDate).ThenByDescending(p => p.Code),
        };

        var projected = ordered.Select(p => new
        {
            p.Id, p.Code, p.CustomerPoNumber, p.CustomerId,
            CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First(),
            p.PoDate, p.Status,
            Total = db.PoLines.Where(l => l.PoId == p.Id).Sum(l => (int?)l.Qty) ?? 0,
            Delivered = db.PoLines.Where(l => l.PoId == p.Id).Sum(l => (int?)l.DeliveredQty) ?? 0,
            Allocated = db.PoLines.Where(l => l.PoId == p.Id).Sum(l => (int?)l.AllocatedQty) ?? 0,
            InProduction = db.PoLines.Where(l => l.PoId == p.Id).Sum(l => (int?)l.InProductionQty) ?? 0,
            Late = (p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress)
                   && db.PoLines.Any(l => l.PoId == p.Id && l.DeliveryDate < today && l.DeliveredQty < l.Qty),
        });

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await projected.CountAsync(ct);
        var rows = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(r => new PoListItem(
            r.Id, r.Code, r.CustomerPoNumber, r.CustomerId, r.CustomerName, r.PoDate, r.Status,
            r.Total, r.Delivered, r.Allocated, r.InProduction, r.Late)).ToList();
        return new PagedResult<PoListItem>(items, page, pageSize, total);
    }
}

public sealed class ListPurchaseOrdersHandler(PoQueries queries)
{
    public Task<PagedResult<PoListItem>> HandleAsync(PoListQuery query, CancellationToken ct) => queries.ListAsync(query, ct);
}

public sealed class GetPurchaseOrderHandler(PoQueries queries)
{
    public Task<PoDto> HandleAsync(Guid id, CancellationToken ct) => queries.GetDetailAsync(id, ct);
}
