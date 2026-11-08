using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Access;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Features.Access.Auth;

/// <summary>
/// Login (AC-AM-01 s.d. AC-AM-06a): pesan galat generik, penguncian akun, pencabutan sesi lama (satu sesi aktif),
/// seluruh perubahan dan audit dalam satu transaksi.
/// </summary>
public sealed class LoginHandler(
    GmsDbContext db,
    UserManager<AppUser> users,
    TimeProvider time,
    IAuditWriter audit,
    IOptions<AuthOptions> authOptions,
    IHttpContextAccessor http)
{
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors.Add("username", "Username wajib diisi.");
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            errors.Add("password", "Password wajib diisi.");
        }

        errors.ThrowIfAny();

        var ip = request.IpAddress ?? http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = request.UserAgent ?? http.HttpContext?.Request.Headers.UserAgent.ToString();
        var username = request.Username.Trim();

        var user = await users.FindByNameAsync(username);
        if (user is null)
        {
            audit.Add(AuditEvents.LoginFailed, newValue: new { reason = "UnknownUser" }, actorUsername: username);
            await db.SaveChangesAsync(ct);
            throw ProblemException.InvalidCredentials();
        }

        if (await users.IsLockedOutAsync(user))
        {
            audit.Add(AuditEvents.LoginFailed, "User", user.Id.ToString(), newValue: new { reason = "Locked" },
                actorUserId: user.Id, actorUsername: user.UserName);
            await db.SaveChangesAsync(ct);
            throw ProblemException.AccountLocked(user.LockoutEnd);
        }

        if (!user.IsActive)
        {
            // Pesan sama dengan password salah (AC-AM-04) dan tidak menambah hitungan kegagalan.
            audit.Add(AuditEvents.LoginFailed, "User", user.Id.ToString(), newValue: new { reason = "Inactive" },
                actorUserId: user.Id, actorUsername: user.UserName);
            await db.SaveChangesAsync(ct);
            throw ProblemException.InvalidCredentials();
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            var locked = await users.IsLockedOutAsync(user);
            audit.Add(AuditEvents.LoginFailed, "User", user.Id.ToString(), newValue: new { reason = "WrongPassword" },
                actorUserId: user.Id, actorUsername: user.UserName);
            if (locked)
            {
                audit.Add(AuditEvents.AccountLocked, "User", user.Id.ToString(),
                    newValue: new { until = user.LockoutEnd }, actorUserId: user.Id, actorUsername: user.UserName);
            }

            await db.SaveChangesAsync(ct);
            throw locked ? ProblemException.AccountLocked(user.LockoutEnd) : ProblemException.InvalidCredentials();
        }

        var now = time.GetUtcNow();
        var token = SessionTokens.Generate();

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await users.ResetAccessFailedCountAsync(user);

            // Satu sesi aktif per pengguna: cabut sesi lama (alasan Replaced) sebelum membuat yang baru.
            var replaced = await db.UserSessions
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(x => x.RevokedReason, (SessionRevokedReason?)SessionRevokedReason.Replaced), ct);

            db.UserSessions.Add(new UserSession
            {
                UserId = user.Id,
                TokenHash = SessionTokens.Hash(token),
                CreatedAt = now,
                LastActivityAt = now,
                IpAddress = Truncate(ip, 64),
                UserAgent = Truncate(userAgent, 500),
            });

            user.LastLoginAt = now;
            audit.Add(AuditEvents.LoginSucceeded, "User", user.Id.ToString(), actorUserId: user.Id, actorUsername: user.UserName);
            if (replaced > 0)
            {
                audit.Add(AuditEvents.SessionReplaced, "User", user.Id.ToString(),
                    newValue: new { replacedSessions = replaced }, actorUserId: user.Id, actorUsername: user.UserName);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var role = await db.Roles.AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => new { r.Name, r.IsSystem })
            .SingleAsync(ct);
        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == user.RoleId)
            .Select(rp => rp.PermissionKey)
            .OrderBy(k => k)
            .ToListAsync(ct);

        return new LoginResponse(
            token,
            authOptions.Value.SessionTimeoutMinutes,
            new CurrentUserDto(user.Id, user.UserName ?? username, user.FullName, user.RoleId, role.Name, role.IsSystem),
            user.MustChangePassword,
            permissions);
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
