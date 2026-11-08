using System.Linq.Expressions;
using Gms.Api.Common;
using Gms.Domain.Master;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Master;

/// <summary>CRUD data dasar FG. Size Berlaku, routing, dan tarif ada di <see cref="FgStructureHandler"/>.</summary>
public sealed class FgHandler(MasterContext ctx) : MasterCrudHandler<Fg, FgListItem, FgDto, SaveFgRequest>(ctx)
{
    protected override string Label => "FG";

    protected override Expression<Func<Fg, FgListItem>> ListSelector =>
        e => new FgListItem(
            e.Id, e.Code, e.Name, e.Kind,
            Db.Customers.Where(c => c.Id == e.CustomerId).Select(c => c.Name).FirstOrDefault(),
            false, e.IsActive);

    protected override Expression<Func<Fg, FgDto>> DetailSelector =>
        e => new FgDto(
            e.Id, e.Code, e.Name, e.Kind, e.CustomerId,
            Db.Customers.Where(c => c.Id == e.CustomerId).Select(c => c.Name).FirstOrDefault(),
            e.Notes, e.IsActive, false,
            Db.PoLines.Any(l => l.FgId == e.Id) || Db.WorkOrders.Any(w => w.FgId == e.Id) || Db.StockBalances.Any(s => s.FgId == e.Id),
            e.RowVersion);

    protected override string? GetCode(SaveFgRequest request) => request.Code;
    protected override uint GetRowVersion(SaveFgRequest request) => request.RowVersion;

    /// <summary>Daftar FG dengan kolom Kesiapan dan filter opsional "hanya Siap Produksi".</summary>
    public override async Task<PagedResult<FgListItem>> ListAsync(MasterListQuery query, CancellationToken ct)
    {
        var set = Db.Fgs.AsNoTracking().AsQueryable();
        set = query.Status switch
        {
            ActiveFilter.Active => set.Where(f => f.IsActive),
            ActiveFilter.Inactive => set.Where(f => !f.IsActive),
            _ => set,
        };

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            set = set.Where(f => EF.Functions.ILike(f.Code, pattern) || EF.Functions.ILike(f.Name, pattern));
        }

        if (query is FgListQuery { ReadyOnly: true })
        {
            set = set.Where(FgReadiness.IsReady(Db));
        }

        var page = await set.OrderBy(f => f.Code).Select(ListSelector).ToPagedAsync(query.Page, query.PageSize, ct);
        var ready = await FgReadiness.ReadyIdsAsync(Db, page.Items.Select(i => i.Id).ToList(), ct);
        var items = page.Items.Select(i => i with { IsReady = ready.Contains(i.Id) }).ToList();
        return page with { Items = items };
    }

    public override async Task<FgDto> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await base.GetAsync(id, ct);
        var ready = await FgReadiness.ReadyIdsAsync(Db, [id], ct);
        return dto with { IsReady = ready.Contains(id) };
    }

    protected override async Task ValidateAsync(SaveFgRequest request, Fg? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 200, errors);
        RequireText(request.Kind, "kind", "Jenis", 100, errors, required: false);
        RequireText(request.Notes, "notes", "Keterangan", 1000, errors, required: false);

        if (request.CustomerId is { } customerId && existing?.CustomerId != customerId
            && !await Db.Customers.AnyAsync(c => c.Id == customerId && c.IsActive, ct))
        {
            errors.Add("customerId", "Customer tidak ditemukan atau nonaktif.");
        }
    }

    protected override void Apply(SaveFgRequest request, Fg entity)
    {
        entity.Name = request.Name.Trim();
        entity.Kind = Clean(request.Kind);
        entity.CustomerId = request.CustomerId;
        entity.Notes = Clean(request.Notes);
    }

    protected override object Snapshot(Fg entity) =>
        new { entity.Code, entity.Name, entity.Kind, entity.CustomerId, entity.IsActive };

    protected override async Task<bool> IsCodeUsedAsync(Fg entity, CancellationToken ct) =>
        await Db.PoLines.AnyAsync(l => l.FgId == entity.Id, ct)
        || await Db.WorkOrders.AnyAsync(w => w.FgId == entity.Id, ct)
        || await Db.StockBalances.AnyAsync(s => s.FgId == entity.Id, ct)
        || await Db.StockLedger.AnyAsync(s => s.FgId == entity.Id, ct);

    /// <summary>AC-MD-18: FG dengan PO atau WO terbuka tidak dapat dinonaktifkan.</summary>
    protected override async Task CheckCanDeactivateAsync(Fg entity, CancellationToken ct)
    {
        var openPos = await Db.PoLines
            .Where(l => l.FgId == entity.Id)
            .Join(Db.PurchaseOrders.Where(p => p.Status != PoStatus.Completed && p.Status != PoStatus.Cancelled),
                l => l.PoId, p => p.Id, (l, p) => "PO " + p.Code)
            .Distinct().Take(10).ToListAsync(ct);
        var openWos = await Db.WorkOrders
            .Where(w => w.FgId == entity.Id && w.Status != WoStatus.Closed && w.Status != WoStatus.Cancelled)
            .Select(w => "WO " + w.Code)
            .Take(10).ToListAsync(ct);

        var users = openPos.Concat(openWos).ToList();
        if (users.Count > 0)
        {
            throw InUse("FG masih dipakai oleh PO/WO yang belum selesai.", users, ProblemCodes.FgHasOpenDocuments);
        }
    }
}
