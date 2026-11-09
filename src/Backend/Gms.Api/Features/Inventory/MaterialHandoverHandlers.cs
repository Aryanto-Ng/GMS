using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Inventory;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Inventory;

/// <summary>Catatan serah terima material Comfi → Konveksi untuk sebuah WO; tidak memengaruhi stok apa pun (AC-ST-23..27).</summary>
public sealed class CreateMaterialHandoverHandler(GmsDbContext db, AppTimeZone zone, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public const decimal MaxQty = 999_999_999m;

    public async Task<Guid> HandleAsync(SaveMaterialHandoverRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        HandoverRules.ValidateHeader(request.Date, zone.Today(time), request.DocNo, null, request.Notes, errors);
        var lines = request.Lines ?? [];
        if (lines.Count == 0)
        {
            errors.Add("lines", "Minimal satu baris material.");
        }

        var materialIds = lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Join(db.Uoms, m => m.UomId, u => u.Id, (m, u) => new { m.Id, m.Code, m.IsActive, UomCode = u.Code })
            .ToDictionaryAsync(x => x.Id, ct);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!materials.TryGetValue(line.MaterialId, out var material))
            {
                errors.Add($"lines[{i}].materialId", "Material tidak ditemukan.");
            }
            else if (!material.IsActive)
            {
                errors.Add($"lines[{i}].materialId", $"Material {material.Code} nonaktif.");
            }

            if (line.Qty <= 0 || line.Qty > MaxQty)
            {
                errors.Add($"lines[{i}].qty", "Qty harus lebih dari 0.");
            }
            else if (decimal.Round(line.Qty, 3) != line.Qty)
            {
                errors.Add($"lines[{i}].qty", "Qty maksimal 3 digit di belakang koma.");
            }

            if (line.Notes is { Length: > 200 })
            {
                errors.Add($"lines[{i}].notes", "Keterangan maksimal 200 karakter.");
            }
        }

        errors.ThrowIfAny();

        var woId = request.WoId;
        var wo = await db.WorkOrders.AsNoTracking().Where(w => w.Id == woId).Select(w => new { w.Status, w.Code }).SingleOrDefaultAsync(ct)
                 ?? throw ProblemException.NotFound("WO");
        if (wo.Status is WoStatus.Closed or WoStatus.Cancelled)
        {
            throw ProblemException.StateConflict($"WO berstatus {wo.Status} tidak menerima serah terima material.", Gms.Shared.Problems.ProblemCodes.WoStatusInvalid);
        }

        var doc = new MaterialHandover
        {
            WoId = woId, HandoverDate = request.Date, DocNo = HandoverRules.Clean(request.DocNo), Notes = HandoverRules.Clean(request.Notes),
            CreatedAt = time.GetUtcNow(), CreatedBy = user.Id,
        };
        foreach (var line in lines)
        {
            doc.Lines.Add(new Gms.Domain.Inventory.MaterialHandoverLine
            {
                HandoverId = doc.Id, MaterialId = line.MaterialId, Qty = line.Qty, UomCode = materials[line.MaterialId].UomCode,
                Notes = HandoverRules.Clean(line.Notes),
            });
        }

        db.MaterialHandovers.Add(doc);
        audit.Add(AuditEvents.MaterialHandoverCreated, "MaterialHandover", doc.Id.ToString(), null,
            new { woId, request.Date, lines = lines.Count });
        await db.SaveChangesAsync(ct);
        return doc.Id;
    }
}

public sealed class VoidMaterialHandoverHandler(GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task HandleAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        var reason = HandoverRules.RequireReason(request.Reason, "void");
        var doc = await db.MaterialHandovers.SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw ProblemException.NotFound("Serah terima material");
        if (doc.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Dokumen sudah di-void dan tidak dapat diaktifkan kembali.");
        }

        var status = await db.WorkOrders.AsNoTracking().Where(w => w.Id == doc.WoId).Select(w => w.Status).SingleAsync(ct);
        if (status is WoStatus.Closed or WoStatus.Cancelled)
        {
            throw ProblemException.StateConflict($"Void tidak diizinkan pada WO berstatus {status}.", Gms.Shared.Problems.ProblemCodes.WoStatusInvalid);
        }

        doc.Status = DocStatus.Voided;
        doc.VoidReason = reason;
        doc.VoidedAt = time.GetUtcNow();
        doc.VoidedBy = user.Id;
        audit.Add(AuditEvents.MaterialHandoverVoided, "MaterialHandover", id.ToString(), new { status = "Active" }, new { status = "Voided", reason });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class MaterialHandoverQueries(GmsDbContext db, ICurrentUser user)
{
    private sealed class Row
    {
        public Guid Id { get; set; }
        public DateOnly Date { get; set; }
        public string? DocNo { get; set; }
        public Guid WoId { get; set; }
        public string WoCode { get; set; } = string.Empty;
        public WoStatus WoStatus { get; set; }
        public int ItemCount { get; set; }
        public DocStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedByName { get; set; }
    }

    private IQueryable<Row> Base() =>
        from h in db.MaterialHandovers.AsNoTracking()
        join w in db.WorkOrders.AsNoTracking() on h.WoId equals w.Id
        select new Row
        {
            Id = h.Id, Date = h.HandoverDate, DocNo = h.DocNo, WoId = w.Id, WoCode = w.Code, WoStatus = w.Status,
            ItemCount = db.MaterialHandoverLines.Count(l => l.HandoverId == h.Id), Status = h.Status, CreatedAt = h.CreatedAt,
            CreatedByName = db.Users.Where(u => u.Id == h.CreatedBy).Select(u => u.FullName).FirstOrDefault(),
        };

    private static MaterialHandoverListItem ToItem(Row r) => new(r.Id, r.Date, r.DocNo, r.WoId, r.WoCode, r.ItemCount, r.Status, r.CreatedByName);

    public async Task<PagedResult<MaterialHandoverListItem>> ListAsync(MaterialHandoverListQuery query, CancellationToken ct)
    {
        var rows = Base();
        if (query.WoId is { } woId) { rows = rows.Where(r => r.WoId == woId); }
        if (query.From is { } from) { rows = rows.Where(r => r.Date >= from); }
        if (query.To is { } to) { rows = rows.Where(r => r.Date <= to); }
        if (query.Status is { } status) { rows = rows.Where(r => r.Status == status); }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            rows = rows.Where(r => EF.Functions.ILike(r.WoCode, pattern) || EF.Functions.ILike(r.DocNo ?? "", pattern));
        }

        var ordered = rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await ordered.CountAsync(ct);
        var pageRows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<MaterialHandoverListItem>(pageRows.Select(ToItem).ToList(), page, pageSize, total);
    }

    public async Task<MaterialHandoverDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Base().Where(r => r.Id == id).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("Serah terima material");
        var doc = await db.MaterialHandovers.AsNoTracking().SingleAsync(h => h.Id == id, ct);
        var lines = await (
            from l in db.MaterialHandoverLines.AsNoTracking()
            where l.HandoverId == id
            join m in db.Materials on l.MaterialId equals m.Id
            orderby m.Code
            select new MaterialHandoverLineDto(m.Id, m.Code, m.Name, l.UomCode, l.Qty, l.Notes)).ToListAsync(ct);
        var canVoid = row.Status == DocStatus.Active && row.WoStatus is not (WoStatus.Closed or WoStatus.Cancelled)
                      && user.HasPermission(Permissions.SerahTerimaMaterial.Void);
        return new MaterialHandoverDto(ToItem(row), lines, doc.Notes, doc.VoidReason, canVoid);
    }
}
