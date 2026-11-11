using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Users;

/// <summary>
/// Reset password oleh Administrator (AC-AM-05, AC-AM-08): password baru (atau acak), wajib diganti saat login,
/// sesi pengguna dicabut, dan kunci akun dibuka. Password sementara hanya dikembalikan sekali dan tidak dicatat.
/// </summary>
public sealed class ResetPasswordHandler(
    GmsDbContext db,
    UserManager<AppUser> users,
    ICurrentUser current,
    IAuditWriter audit,
    TimeProvider time)
{
    public async Task<ResetPasswordResponse> HandleAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw ProblemException.NotFound("Pengguna");
        await SystemRoleGuard.EnsureCanManageUserAsync(db, current, id, ct);
        var password = string.IsNullOrEmpty(request.NewPassword) ? TemporaryPassword.Generate() : request.NewPassword;
        var now = time.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await users.HasPasswordAsync(user))
        {
            var removed = await users.RemovePasswordAsync(user);
            if (!removed.Succeeded)
            {
                throw IdentityErrorMapper.ToProblem(removed.Errors, "newPassword");
            }
        }

        var added = await users.AddPasswordAsync(user, password);
        if (!added.Succeeded)
        {
            throw IdentityErrorMapper.ToProblem(added.Errors, "newPassword");
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);

        user.MustChangePassword = true;
        user.UpdatedAt = now;
        user.UpdatedBy = current.Id;

        await db.UserSessions
            .Where(s => s.UserId == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, (DateTimeOffset?)now)
                .SetProperty(x => x.RevokedReason, (SessionRevokedReason?)SessionRevokedReason.PasswordReset), ct);

        // Sengaja tanpa nilai password pada audit (AC-AM-26).
        audit.Add(AuditEvents.PasswordReset, "User", id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ResetPasswordResponse(password);
    }
}
