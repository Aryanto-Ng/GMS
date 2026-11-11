using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Users;

/// <summary>
/// Mengubah nama lengkap dan Role. Username tidak dapat diubah (AC-AM-12).
/// Administrator aktif terakhir tidak boleh diturunkan Role-nya (AC-AM-13).
/// </summary>
public sealed class UpdateUserHandler(GmsDbContext db, ICurrentUser current, IAuditWriter audit, TimeProvider time)
{
    public async Task<UserDetail> HandleAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        var fullName = request.FullName?.Trim() ?? string.Empty;
        if (fullName.Length is 0 or > 200)
        {
            errors.Add("fullName", "Nama lengkap wajib diisi (maksimal 200 karakter).");
        }

        var newRole = await db.Roles.AsNoTracking()
            .Where(r => r.Id == request.RoleId)
            .Select(r => new { r.Id, r.Name })
            .SingleOrDefaultAsync(ct);
        if (newRole is null)
        {
            errors.Add("roleId", "Role tidak ditemukan.");
        }

        errors.ThrowIfAny();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw ProblemException.NotFound("Pengguna");
        await SystemRoleGuard.EnsureCanManageUserAsync(db, current, id, ct);
        await SystemRoleGuard.EnsureCanAssignRoleAsync(db, current, newRole!.Id, ct);
        SystemRoleGuard.EnsureNotOwnRoleChange(current, id, user.RoleId != newRole.Id);
        db.Entry(user).Property(u => u.RowVersion).OriginalValue = request.RowVersion;

        var oldRoleName = await db.Roles.Where(r => r.Id == user.RoleId).Select(r => r.Name).SingleAsync(ct);
        var roleChanged = user.RoleId != newRole!.Id;

        if (roleChanged && user.IsActive && await UserQueries.IsLastActiveAdministratorAsync(db, user.Id, user.RoleId, ct))
        {
            throw ProblemException.Rule(ProblemCodes.LastAdministrator,
                "Administrator aktif terakhir tidak dapat diubah Role-nya. Buat Administrator lain terlebih dahulu.");
        }

        var oldValue = new { fullName = user.FullName, role = oldRoleName };
        var now = time.GetUtcNow();
        user.FullName = fullName;
        user.RoleId = newRole.Id;
        user.UpdatedAt = now;
        user.UpdatedBy = current.Id;

        audit.Add(AuditEvents.UserUpdated, "User", user.Id.ToString(), oldValue, new { fullName, role = newRole.Name });
        await db.SaveChangesAsync(ct);

        return await UserQueries.GetDetailAsync(db, id, now, ct);
    }
}
