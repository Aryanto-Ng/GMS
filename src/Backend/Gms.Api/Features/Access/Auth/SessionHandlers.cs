using System.Security.Claims;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Auth;

/// <summary>Logout: mencabut sesi saat ini (alasan Logout).</summary>
public sealed class LogoutHandler(GmsDbContext db, ICurrentUser current, IAuditWriter audit, TimeProvider time)
{
    public async Task HandleAsync(CancellationToken ct)
    {
        var sessionId = current.SessionId ?? throw ProblemException.Forbidden();
        var now = time.GetUtcNow();

        await db.UserSessions
            .Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, (DateTimeOffset?)now)
                .SetProperty(x => x.RevokedReason, (SessionRevokedReason?)SessionRevokedReason.Logout), ct);

        audit.Add(AuditEvents.Logout, "User", current.Id?.ToString());
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Profil pengguna saat ini + permission (dari klaim yang dibangun handler autentikasi; tanpa query tambahan).</summary>
public sealed class MeHandler(IHttpContextAccessor http)
{
    public MeResponse Handle()
    {
        var principal = http.HttpContext?.User ?? throw ProblemException.Forbidden();
        string Claim(string type) => principal.FindFirstValue(type) ?? string.Empty;

        var user = new CurrentUserDto(
            Guid.Parse(Claim(ClaimTypes.NameIdentifier)),
            principal.Identity?.Name ?? string.Empty,
            Claim(GmsClaims.FullName),
            Guid.Parse(Claim(GmsClaims.RoleId)),
            Claim(GmsClaims.RoleName),
            Claim(GmsClaims.IsAdministrator) == "true");

        var permissions = principal.FindAll(GmsClaims.Permission).Select(c => c.Value).OrderBy(k => k).ToList();
        return new MeResponse(user, Claim(GmsClaims.MustChangePassword) == "true", permissions);
    }
}

/// <summary>Ganti password sendiri (AC-AM-09). Menghapus kewajiban ganti password bila sebelumnya diwajibkan.</summary>
public sealed class ChangePasswordHandler(
    GmsDbContext db,
    UserManager<AppUser> users,
    ICurrentUser current,
    IAuditWriter audit,
    TimeProvider time)
{
    public async Task HandleAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            errors.Add("currentPassword", "Password lama wajib diisi.");
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            errors.Add("newPassword", "Password baru wajib diisi.");
        }
        else if (request.NewPassword == request.CurrentPassword)
        {
            errors.Add("newPassword", "Password baru tidak boleh sama dengan password lama.");
        }

        errors.ThrowIfAny();

        var userId = current.Id ?? throw ProblemException.Forbidden();
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw ProblemException.NotFound("Pengguna");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            throw IdentityErrorMapper.ToProblem(result.Errors, "newPassword");
        }

        user.MustChangePassword = false;
        user.UpdatedAt = time.GetUtcNow();
        user.UpdatedBy = userId;
        audit.Add(AuditEvents.PasswordChanged, "User", user.Id.ToString());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
