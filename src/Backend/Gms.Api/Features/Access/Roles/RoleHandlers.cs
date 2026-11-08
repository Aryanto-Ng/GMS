using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Access;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Roles;

public sealed class ListRolesHandler(GmsDbContext db)
{
    public async Task<IReadOnlyList<RoleListItem>> HandleAsync(CancellationToken ct) =>
        await db.Roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new RoleListItem(
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                db.Users.Count(u => u.RoleId == r.Id),
                db.RolePermissions.Count(rp => rp.RoleId == r.Id)))
            .ToListAsync(ct);
}

public sealed class GetRoleHandler(GmsDbContext db)
{
    public Task<RoleDetail> HandleAsync(Guid id, CancellationToken ct) => RoleQueries.GetDetailAsync(db, id, ct);
}

public sealed class GetPermissionCatalogHandler(GmsDbContext db)
{
    public async Task<IReadOnlyList<PermissionItem>> HandleAsync(CancellationToken ct) =>
        await db.Permissions.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .Select(p => new PermissionItem(p.Key, p.Resource, p.Action, p.Module, p.Description, p.SortOrder, p.IsAssignable))
            .ToListAsync(ct);
}

public sealed class CreateRoleHandler(GmsDbContext db, ICurrentUser current, IAuditWriter audit, TimeProvider time)
{
    public async Task<RoleDetail> HandleAsync(SaveRoleRequest request, CancellationToken ct)
    {
        var (name, description, keys) = await RoleQueries.ValidateAsync(db, null, request, ct);

        var role = new AppRole
        {
            Name = name,
            Description = description,
            IsSystem = false,
            CreatedAt = time.GetUtcNow(),
            CreatedBy = current.Id,
        };
        foreach (var key in keys)
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
        }

        db.Roles.Add(role);
        audit.Add(AuditEvents.RoleCreated, "Role", role.Id.ToString(),
            newValue: new { name, description, permissions = keys.Order().ToList() });
        await db.SaveChangesAsync(ct);

        return await RoleQueries.GetDetailAsync(db, role.Id, ct);
    }
}

/// <summary>
/// Mengubah Role. Role sistem (Administrator) tidak dapat diubah (AC-AM-16). Perubahan permission berlaku pada aksi
/// pengguna berikutnya tanpa login ulang (AC-AM-19), karena permission dibaca dari database pada setiap permintaan.
/// </summary>
public sealed class UpdateRoleHandler(GmsDbContext db, ICurrentUser current, IAuditWriter audit, TimeProvider time)
{
    public async Task<RoleDetail> HandleAsync(Guid id, SaveRoleRequest request, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw ProblemException.NotFound("Role");

        if (role.IsSystem)
        {
            throw ProblemException.StateConflict("Role sistem tidak dapat diubah.", ProblemCodes.SystemRoleImmutable);
        }

        db.Entry(role).Property(r => r.RowVersion).OriginalValue = request.RowVersion;
        var (name, description, keys) = await RoleQueries.ValidateAsync(db, id, request, ct);

        var oldKeys = role.Permissions.Select(p => p.PermissionKey).ToHashSet(StringComparer.Ordinal);
        var newKeys = keys.ToHashSet(StringComparer.Ordinal);
        var added = newKeys.Except(oldKeys).Order().ToList();
        var removed = oldKeys.Except(newKeys).Order().ToList();
        var oldValue = new { name = role.Name, description = role.Description };

        role.Name = name;
        role.Description = description;

        // Baris role selalu "disentuh" agar xmin berubah: dua penyunting yang bersamaan terdeteksi walau hanya matriks yang berbeda.
        role.UpdatedAt = time.GetUtcNow();
        role.UpdatedBy = current.Id;

        foreach (var permission in role.Permissions.Where(p => removed.Contains(p.PermissionKey)).ToList())
        {
            role.Permissions.Remove(permission);
            db.RolePermissions.Remove(permission);
        }

        foreach (var key in added)
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
        }

        audit.Add(AuditEvents.RoleUpdated, "Role", id.ToString(), oldValue, new { name, description, added, removed });
        await db.SaveChangesAsync(ct);

        return await RoleQueries.GetDetailAsync(db, id, ct);
    }
}

public sealed class DeleteRoleHandler(GmsDbContext db, IAuditWriter audit)
{
    public async Task HandleAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw ProblemException.NotFound("Role");

        if (role.IsSystem)
        {
            throw ProblemException.StateConflict("Role sistem tidak dapat dihapus.", ProblemCodes.SystemRoleImmutable);
        }

        var userCount = await db.Users.CountAsync(u => u.RoleId == id, ct);
        if (userCount > 0)
        {
            throw ProblemException.Rule(ProblemCodes.RoleInUse,
                $"Role masih dipakai oleh {userCount} pengguna. Pindahkan pengguna ke Role lain terlebih dahulu.");
        }

        db.Roles.Remove(role);
        audit.Add(AuditEvents.RoleDeleted, "Role", id.ToString(), oldValue: new { name = role.Name });
        await db.SaveChangesAsync(ct);
    }
}
