using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Users;

/// <summary>Membuka kunci akun lebih awal (AC-AM-03).</summary>
public sealed class UnlockUserHandler(GmsDbContext db, UserManager<AppUser> users, ICurrentUser current, IAuditWriter audit)
{
    public async Task HandleAsync(Guid id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw ProblemException.NotFound("Pengguna");
        await SystemRoleGuard.EnsureCanManageUserAsync(db, current, id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        audit.Add(AuditEvents.UserUnlocked, "User", id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

/// <summary>Mengakhiri paksa sesi aktif pengguna (AC-AM-06b). Idempoten bila tidak ada sesi aktif.</summary>
public sealed class EndUserSessionHandler(GmsDbContext db, ICurrentUser current, IAuditWriter audit, TimeProvider time)
{
    public async Task HandleAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            throw ProblemException.NotFound("Pengguna");
        }

        await SystemRoleGuard.EnsureCanManageUserAsync(db, current, id, ct);

        var ended = await db.UserSessions
            .Where(s => s.UserId == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, (DateTimeOffset?)time.GetUtcNow())
                .SetProperty(x => x.RevokedReason, (SessionRevokedReason?)SessionRevokedReason.AdminForced), ct);

        if (ended > 0)
        {
            audit.Add(AuditEvents.SessionEnded, "User", id.ToString());
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// Menonaktifkan/mengaktifkan pengguna. Pengguna tidak dihapus (AC-AM-11). Tidak dapat menonaktifkan diri sendiri
/// (AC-AM-14) atau Administrator aktif terakhir (AC-AM-13).
/// </summary>
public sealed class SetUserActiveHandler(GmsDbContext db, ICurrentUser current, IAuditWriter audit, TimeProvider time)
{
    public async Task<UserDetail> HandleAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw ProblemException.NotFound("Pengguna");
        await SystemRoleGuard.EnsureCanManageUserAsync(db, current, id, ct);
        db.Entry(user).Property(u => u.RowVersion).OriginalValue = rowVersion;
        var now = time.GetUtcNow();

        if (!isActive)
        {
            if (current.Id == id)
            {
                throw ProblemException.Rule(ProblemCodes.CannotDeactivateSelf, "Anda tidak dapat menonaktifkan akun Anda sendiri.");
            }

            if (user.IsActive && await UserQueries.IsLastActiveAdministratorAsync(db, id, user.RoleId, ct))
            {
                throw ProblemException.Rule(ProblemCodes.LastAdministrator,
                    "Administrator aktif terakhir tidak dapat dinonaktifkan. Buat Administrator lain terlebih dahulu.");
            }
        }

        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            user.UpdatedAt = now;
            user.UpdatedBy = current.Id;

            if (!isActive)
            {
                await db.UserSessions
                    .Where(s => s.UserId == id && s.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.RevokedAt, (DateTimeOffset?)now)
                        .SetProperty(x => x.RevokedReason, (SessionRevokedReason?)SessionRevokedReason.Deactivated), ct);
            }

            audit.Add(isActive ? AuditEvents.UserActivated : AuditEvents.UserDeactivated, "User", id.ToString());
            await db.SaveChangesAsync(ct);
        }

        return await UserQueries.GetDetailAsync(db, id, now, ct);
    }
}
