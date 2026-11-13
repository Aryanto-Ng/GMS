using Gms.Infrastructure.Persistence;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Notifications;

/// <summary>
/// Penentuan penerima (FSD bagian 2): pengguna AKTIF yang memiliki SEMUA izin pada definisi jenis, kecuali pelaku.
/// Satu query; izin dibaca dari <c>role_permissions</c> pada saat notifikasi dibuat (AC-NT-02).
/// </summary>
internal static class NotificationRecipients
{
    public static async Task<List<Guid>> ResolveAsync(GmsDbContext db, NotificationTypeDefinition def, Guid? actorUserId, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (actorUserId is { } actor)
        {
            query = query.Where(u => u.Id != actor);
        }

        foreach (var permission in def.RequiredPermissions)
        {
            var key = permission;
            query = query.Where(u => db.RolePermissions.Any(rp => rp.RoleId == u.RoleId && rp.PermissionKey == key));
        }

        return await query.Select(u => u.Id).ToListAsync(ct);
    }
}
