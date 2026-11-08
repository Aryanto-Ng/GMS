using Gms.Api.Common;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Roles;

internal static class RoleQueries
{
    public static async Task<RoleDetail> GetDetailAsync(GmsDbContext db, Guid id, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Id, r.Name, r.Description, r.IsSystem, r.RowVersion })
            .SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("Role");

        var keys = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == id)
            .Select(rp => rp.PermissionKey)
            .OrderBy(k => k)
            .ToListAsync(ct);
        var userCount = await db.Users.CountAsync(u => u.RoleId == id, ct);

        return new RoleDetail(role.Id, role.Name, role.Description, role.IsSystem, userCount, keys, role.RowVersion);
    }

    /// <summary>
    /// Memvalidasi nama + permission untuk simpan Role: nama unik, permission dikenal dan dapat diberikan
    /// (bukan milik Administrator), dan aturan View otomatis (AC-AM-18): aksi apa pun menyertakan View pada resource yang sama.
    /// </summary>
    public static async Task<(string Name, string? Description, List<string> Keys)> ValidateAsync(
        GmsDbContext db, Guid? existingId, SaveRoleRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        var name = request.Name?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        if (name.Length is 0 or > 100)
        {
            errors.Add("name", "Nama role wajib diisi (maksimal 100 karakter).");
        }

        if (description is { Length: > 500 })
        {
            errors.Add("description", "Deskripsi maksimal 500 karakter.");
        }

        var keys = (request.PermissionKeys ?? []).Distinct(StringComparer.Ordinal).ToList();
        var known = await db.Permissions.AsNoTracking()
            .Select(p => new { p.Key, p.IsAssignable })
            .ToDictionaryAsync(p => p.Key, p => p.IsAssignable, ct);
        var selected = keys.ToHashSet(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (!known.TryGetValue(key, out var assignable))
            {
                errors.Add("permissionKeys", $"Permission '{key}' tidak dikenal.");
                continue;
            }

            if (!assignable)
            {
                errors.Add("permissionKeys", $"Permission '{key}' hanya untuk Administrator dan tidak dapat diberikan ke Role lain.");
            }

            if (PermissionCatalog.RequiredViewFor(key) is { } required && !selected.Contains(required))
            {
                errors.Add("permissionKeys", $"Permission '{key}' membutuhkan '{required}' pada resource yang sama.");
            }
        }

        if (name.Length > 0 && await db.Roles.AnyAsync(r => r.Name == name && r.Id != existingId, ct))
        {
            throw ProblemException.Duplicate("name", "Nama role sudah digunakan.");
        }

        errors.ThrowIfAny();
        return (name, description, keys);
    }
}
