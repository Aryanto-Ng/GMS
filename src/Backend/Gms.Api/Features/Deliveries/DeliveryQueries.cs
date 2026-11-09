using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Deliveries;
using Gms.Shared.Enums;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Deliveries;

/// <summary>Daftar dan detail surat jalan (AC-PG-14/15). Aksi mengikuti status dan permission.</summary>
public sealed class DeliveryQueries(GmsDbContext db, ICurrentUser user)
{
    private sealed class Row
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public Guid PoId { get; set; }
        public string PoCode { get; set; } = string.Empty;
        public PoStatus PoStatus { get; set; }
        public string? CustomerPoNumber { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int TotalQty { get; set; }
        public DocStatus Status { get; set; }
        public int PrintCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
    }

    private IQueryable<Row> Base() =>
        from d in db.Deliveries.AsNoTracking()
        join p in db.PurchaseOrders.AsNoTracking() on d.PoId equals p.Id
        select new Row
        {
            Id = d.Id, Code = d.Code, Date = d.DeliveryDate, PoId = p.Id, PoCode = p.Code, PoStatus = p.Status,
            CustomerPoNumber = p.CustomerPoNumber, CustomerId = p.CustomerId,
            CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First(),
            TotalQty = db.DeliveryLines.Where(l => l.DeliveryId == d.Id).Sum(l => (int?)l.Qty) ?? 0,
            Status = d.Status, PrintCount = d.PrintCount, CreatedAt = d.CreatedAt,
            CreatedByName = db.Users.Where(u => u.Id == d.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
        };

    private static DeliveryListItem ToItem(Row r) =>
        new(r.Id, r.Code, r.Date, r.PoId, r.PoCode, r.CustomerPoNumber, r.CustomerName, r.TotalQty, r.Status, r.PrintCount, r.CreatedByName);

    public async Task<PagedResult<DeliveryListItem>> ListAsync(DeliveryListQuery query, CancellationToken ct)
    {
        var rows = Base();
        if (query.PoId is { } poId) { rows = rows.Where(r => r.PoId == poId); }
        if (query.CustomerId is { } customerId) { rows = rows.Where(r => r.CustomerId == customerId); }
        if (query.From is { } from) { rows = rows.Where(r => r.Date >= from); }
        if (query.To is { } to) { rows = rows.Where(r => r.Date <= to); }
        if (query.Status is { } status) { rows = rows.Where(r => r.Status == status); }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            rows = rows.Where(r => EF.Functions.ILike(r.Code, pattern) || EF.Functions.ILike(r.PoCode, pattern) || EF.Functions.ILike(r.CustomerPoNumber ?? "", pattern));
        }

        var ordered = rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Code);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await ordered.CountAsync(ct);
        var pageRows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<DeliveryListItem>(pageRows.Select(ToItem).ToList(), page, pageSize, total);
    }

    public async Task<DeliveryDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Base().Where(r => r.Id == id).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("Surat jalan");
        var doc = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == id, ct);
        var lines = await (
            from l in db.DeliveryLines.AsNoTracking()
            where l.DeliveryId == id
            join p in db.PoLines on l.PoLineId equals p.Id
            join f in db.Fgs on p.FgId equals f.Id
            join c in db.Colors on p.ColorId equals c.Id
            join s in db.Sizes on p.SizeId equals s.Id
            orderby f.Code, c.Code, s.SortOrder
            select new DeliveryLineDto(l.PoLineId, f.Code, f.Name,
                new ColorSizeRef(c.Id, c.Code, c.Name, s.Id, s.Code, s.Name, s.SortOrder), l.Qty)).ToListAsync(ct);
        var replacedBy = await db.Deliveries.AsNoTracking().Where(d => d.ReplacesId == id).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);

        var actions = new List<string>();
        if (row.Status == DocStatus.Active)
        {
            if (user.HasPermission(Permissions.Pengiriman.Edit)) { actions.Add(DeliveryActions.EditInfo); }
            if (row.PoStatus != PoStatus.Cancelled)
            {
                if (user.HasPermission(Permissions.Pengiriman.Void)) { actions.Add(DeliveryActions.Void); }
                if (user.HasPermission(Permissions.Pengiriman.Correct)) { actions.Add(DeliveryActions.Correct); }
            }
        }

        if (user.HasPermission(Permissions.Pengiriman.Print)) { actions.Add(DeliveryActions.Print); }

        return new DeliveryDto(ToItem(row), doc.RecipientName, doc.Vehicle, doc.Driver, doc.Notes, lines, doc.VoidReason, doc.ReplacesId,
            replacedBy, actions, doc.RowVersion);
    }
}
