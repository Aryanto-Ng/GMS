using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Users;

/// <summary>
/// SEC-001: izin pengelolaan pengguna tidak boleh menjadi jalan menuju Administrator. Hanya Administrator (role sistem) yang boleh
/// menetapkan role sistem dan mengelola pengguna yang berrole sistem; pengguna lain tidak boleh mengubah role dirinya sendiri.
/// </summary>
internal static class SystemRoleGuard
{
    private const string Detail = "Hanya Administrator yang dapat mengelola Administrator atau menetapkan role Administrator.";

    /// <summary>Menolak non-Administrator yang menetapkan role sistem.</summary>
    public static async Task EnsureCanAssignRoleAsync(GmsDbContext db, ICurrentUser current, Guid roleId, CancellationToken ct)
    {
        if (!current.IsAdministrator && await db.Roles.AnyAsync(r => r.Id == roleId && r.IsSystem, ct))
        {
            throw ProblemException.Forbidden(Detail);
        }
    }

    /// <summary>Menolak non-Administrator yang mengelola pengguna ber-role sistem (ubah, reset password, buka kunci, akhiri sesi, nonaktif/aktif).</summary>
    public static async Task EnsureCanManageUserAsync(GmsDbContext db, ICurrentUser current, Guid userId, CancellationToken ct)
    {
        if (current.IsAdministrator)
        {
            return;
        }

        var targetIsSystem = await db.Users.AnyAsync(u => u.Id == userId && db.Roles.Any(r => r.Id == u.RoleId && r.IsSystem), ct);
        if (targetIsSystem)
        {
            throw ProblemException.Forbidden(Detail);
        }
    }

    /// <summary>Menolak non-Administrator yang mengubah role dirinya sendiri.</summary>
    public static void EnsureNotOwnRoleChange(ICurrentUser current, Guid userId, bool roleChanged)
    {
        if (!current.IsAdministrator && roleChanged && current.Id == userId)
        {
            throw ProblemException.Forbidden("Anda tidak dapat mengubah role Anda sendiri.");
        }
    }
}
