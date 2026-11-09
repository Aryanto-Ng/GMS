using System.Security.Claims;
using Gms.Shared.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace Gms.Web.Infrastructure.Auth;

/// <summary>Nama klaim pada cookie autentikasi. Token sesi opaque HANYA ada di cookie terenkripsi (HttpOnly), tidak pernah di browser (ADR-002).</summary>
public static class GmsClaims
{
    public const string Scheme = "GmsCookie";
    public const string SessionToken = "gms:session";
    public const string FullName = "gms:full-name";
    public const string RoleName = "gms:role";
    public const string IsAdministrator = "gms:is-admin";
    public const string MustChangePassword = "gms:must-change-password";
    public const string Permission = "gms:perm";

    public static ClaimsPrincipal Create(CurrentUserDto user, string token, bool mustChangePassword, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(FullName, user.FullName),
            new(RoleName, user.RoleName),
            new(IsAdministrator, user.IsAdministrator ? "true" : "false"),
            new(MustChangePassword, mustChangePassword ? "true" : "false"),
            new(SessionToken, token),
        };
        claims.AddRange(permissions.Distinct(StringComparer.Ordinal).Select(p => new Claim(Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static bool HasPermission(this ClaimsPrincipal user, string permission) =>
        user.Identity?.IsAuthenticated == true && user.HasClaim(Permission, permission);

    public static bool HasAnyPermission(this ClaimsPrincipal user, params string[] permissions) => permissions.Any(user.HasPermission);

    public static string DisplayName(this ClaimsPrincipal user) => user.FindFirstValue(FullName) ?? user.Identity?.Name ?? "Pengguna";

    public static bool IsAdmin(this ClaimsPrincipal user) => user.FindFirstValue(IsAdministrator) == "true";

    public static bool MustChangePasswordNow(this ClaimsPrincipal user) => user.FindFirstValue(MustChangePassword) == "true";
}

/// <summary>Menyediakan token sesi untuk panggilan API. Endpoint SSR (login/logout) dapat menetapkan token secara eksplisit.</summary>
public interface ISessionTokenProvider
{
    ValueTask<string?> GetTokenAsync();

    void Override(string? token);
}

public sealed class ClaimsSessionTokenProvider(AuthenticationStateProvider auth) : ISessionTokenProvider
{
    private string? _override;

    public void Override(string? token) => _override = token;

    public async ValueTask<string?> GetTokenAsync()
    {
        if (_override is not null)
        {
            return _override;
        }

        var state = await auth.GetAuthenticationStateAsync();
        return state.User.FindFirstValue(GmsClaims.SessionToken);
    }
}

// ===== Otorisasi berbasis permission: [Authorize(Policy = "perm:WorkOrder.View")] =====

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public static string PolicyFor(string permission) => Prefix + permission;

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder(GmsClaims.Scheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}
