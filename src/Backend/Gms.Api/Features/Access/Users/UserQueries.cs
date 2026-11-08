using Gms.Api.Common;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Users;

/// <summary>Proyeksi bersama untuk detail pengguna (dipakai Get/Create/Update/aksi siklus hidup).</summary>
internal static class UserQueries
{
    public static UserStatus StatusOf(bool isActive, DateTimeOffset? lockoutEnd, DateTimeOffset now) =>
        !isActive ? UserStatus.Inactive
        : lockoutEnd is not null && lockoutEnd > now ? UserStatus.Locked
        : UserStatus.Active;

    public static async Task<UserDetail> GetDetailAsync(GmsDbContext db, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id, u.UserName, u.FullName, u.RoleId, u.IsActive, u.LastLoginAt, u.AccessFailedCount, u.LockoutEnd, u.RowVersion,
            })
            .SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("Pengguna");

        var role = await db.Roles.AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => new { r.Name, r.IsSystem })
            .SingleAsync(ct);

        var session = await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == id && s.RevokedAt == null)
            .Select(s => new { s.IpAddress, s.CreatedAt })
            .SingleOrDefaultAsync(ct);

        var isLastAdmin = role.IsSystem && user.IsActive
            && !await db.Users.AnyAsync(u => u.RoleId == user.RoleId && u.IsActive && u.Id != id, ct);

        var locked = user.LockoutEnd is not null && user.LockoutEnd > now;
        return new UserDetail(
            user.Id,
            user.UserName ?? string.Empty,
            user.FullName,
            user.RoleId,
            role.Name,
            StatusOf(user.IsActive, user.LockoutEnd, now),
            user.LastLoginAt,
            session is not null,
            session?.IpAddress,
            session?.CreatedAt,
            user.AccessFailedCount,
            locked ? user.LockoutEnd : null,
            isLastAdmin,
            user.RowVersion);
    }

    /// <summary>True bila pengguna adalah satu-satunya Administrator aktif (role sistem).</summary>
    public static async Task<bool> IsLastActiveAdministratorAsync(GmsDbContext db, Guid userId, Guid roleId, CancellationToken ct)
    {
        var isSystem = await db.Roles.AnyAsync(r => r.Id == roleId && r.IsSystem, ct);
        return isSystem && !await db.Users.AnyAsync(u => u.RoleId == roleId && u.IsActive && u.Id != userId, ct);
    }
}
