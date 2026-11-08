using System.Security.Claims;
using System.Text.Encodings.Web;
using Gms.Api.Common;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Security;

/// <summary>
/// Autentikasi sesi opaque (ADR-002): memeriksa token bearer terhadap tabel user_sessions pada SETIAP permintaan,
/// sehingga pencabutan sesi, penonaktifan pengguna, dan perubahan permission berlaku pada aksi berikutnya.
/// </summary>
public sealed class GmsSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    GmsDbContext db,
    TimeProvider time,
    IOptions<AuthOptions> authOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "GmsSession";
    private const string FailureItemKey = "gms.auth.failure";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadBearerToken();
        if (token is null)
        {
            return Fail(ProblemTypes.SessionExpired, "Tidak ada token sesi.");
        }

        var ct = Context.RequestAborted;
        var hash = SessionTokens.Hash(token);
        var session = await db.UserSessions.AsNoTracking()
            .Where(s => s.TokenHash == hash)
            .Select(s => new { s.Id, s.UserId, s.LastActivityAt, s.RevokedAt, s.RevokedReason })
            .SingleOrDefaultAsync(ct);

        if (session is null)
        {
            return Fail(ProblemTypes.SessionExpired, "Token tidak dikenal.");
        }

        if (session.RevokedAt is not null)
        {
            var type = session.RevokedReason switch
            {
                SessionRevokedReason.Replaced => ProblemTypes.SessionReplaced,
                SessionRevokedReason.AdminForced or SessionRevokedReason.Deactivated => ProblemTypes.AccountDisabled,
                _ => ProblemTypes.SessionExpired,
            };
            return Fail(type, "Sesi sudah dicabut.");
        }

        var now = time.GetUtcNow();
        var timeout = TimeSpan.FromMinutes(authOptions.Value.SessionTimeoutMinutes);
        if (now - session.LastActivityAt > timeout)
        {
            await db.UserSessions.Where(s => s.Id == session.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, SessionRevokedReason.Timeout), ct);
            return Fail(ProblemTypes.SessionExpired, "Sesi berakhir karena tidak aktif.");
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == session.UserId)
            .Select(u => new { u.Id, u.UserName, u.FullName, u.IsActive, u.MustChangePassword, u.RoleId })
            .SingleAsync(ct);

        if (!user.IsActive)
        {
            await db.UserSessions.Where(s => s.Id == session.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedReason, SessionRevokedReason.Deactivated), ct);
            return Fail(ProblemTypes.AccountDisabled, "Akun dinonaktifkan.");
        }

        var role = await db.Roles.AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => new { r.Name, r.IsSystem })
            .SingleAsync(ct);
        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == user.RoleId)
            .Select(rp => rp.PermissionKey)
            .ToListAsync(ct);

        // Pembaruan aktivitas dibatasi agar tidak menulis di setiap permintaan.
        if (now - session.LastActivityAt >= TimeSpan.FromSeconds(authOptions.Value.ActivityTouchSeconds))
        {
            await db.UserSessions.Where(s => s.Id == session.Id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActivityAt, now), ct);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(GmsClaims.FullName, user.FullName),
            new(GmsClaims.RoleId, user.RoleId.ToString()),
            new(GmsClaims.RoleName, role.Name),
            new(GmsClaims.IsAdministrator, role.IsSystem ? "true" : "false"),
            new(GmsClaims.MustChangePassword, user.MustChangePassword ? "true" : "false"),
            new(GmsClaims.SessionId, session.Id.ToString()),
        };
        claims.AddRange(permissions.Select(p => new Claim(GmsClaims.Permission, p)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var type = Context.Items[FailureItemKey] as string ?? ProblemTypes.SessionExpired;
        var problem = ProblemWriter.ForStatus(StatusCodes.Status401Unauthorized) with
        {
            Type = type,
            Title = TitleFor(type),
            Detail = DetailFor(type),
        };
        await ProblemWriter.WriteAsync(Context, problem);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ProblemWriter.WriteAsync(Context, ProblemWriter.ForStatus(StatusCodes.Status403Forbidden));

    private string? ReadBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && header.Length > prefix.Length
            ? header[prefix.Length..].Trim()
            : null;
    }

    private AuthenticateResult Fail(string problemType, string message)
    {
        Context.Items[FailureItemKey] = problemType;
        return AuthenticateResult.Fail(message);
    }

    private static string TitleFor(string type) => type switch
    {
        ProblemTypes.SessionReplaced => "Sesi digantikan.",
        ProblemTypes.AccountDisabled => "Akun dinonaktifkan.",
        _ => "Sesi berakhir.",
    };

    private static string DetailFor(string type) => type switch
    {
        ProblemTypes.SessionReplaced => "Akun Anda digunakan di perangkat lain. Silakan masuk kembali.",
        ProblemTypes.AccountDisabled => "Akun Anda dinonaktifkan atau sesi diakhiri oleh Administrator.",
        _ => "Sesi Anda berakhir. Silakan masuk kembali.",
    };
}
