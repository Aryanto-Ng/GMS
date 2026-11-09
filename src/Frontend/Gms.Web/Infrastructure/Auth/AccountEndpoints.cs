using System.Security.Claims;
using Gms.Shared.Access;
using Gms.Shared.Problems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Gms.Web.Infrastructure.Auth;

public sealed record LoginForm(string? Username, string? Password, string? ReturnUrl);

public sealed record ChangePasswordForm(string? CurrentPassword, string? NewPassword, string? ConfirmPassword, string? ReturnUrl);

/// <summary>
/// Pola BFF (ADR-002): form Static SSR mengirim ke endpoint ini; server memanggil API, lalu menaruh token sesi opaque di cookie
/// autentikasi terenkripsi (HttpOnly, SameSite=Lax). Browser tidak pernah memegang token. Semua form dilindungi antiforgery.
/// </summary>
public static class AccountEndpoints
{
    public const string LoginPath = "/login";
    public const string ChangePasswordPath = "/account/change-password";

    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account");

        group.MapPost("/login", async ([FromForm] LoginForm form, HttpContext http, IAuthApi auth, ISessionTokenProvider tokens, CancellationToken ct) =>
        {
            var returnUrl = SafeReturnUrl(form.ReturnUrl);
            if (string.IsNullOrWhiteSpace(form.Username) || string.IsNullOrEmpty(form.Password))
            {
                return Results.LocalRedirect(LoginUrl("required", returnUrl));
            }

            try
            {
                tokens.Override(string.Empty); // login tidak membawa token; mencegah pembacaan status autentikasi di luar Blazor
                var response = await auth.LoginAsync(
                    new LoginRequest(form.Username.Trim(), form.Password, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString()), ct);
                await SignInAsync(http, response.User, response.Token, response.MustChangePassword, response.Permissions, response.ExpiresInMinutes);
                return Results.LocalRedirect(response.MustChangePassword ? $"{ChangePasswordPath}?returnUrl={Uri.EscapeDataString(returnUrl)}" : returnUrl);
            }
            catch (ApiException ex)
            {
                return Results.LocalRedirect(LoginUrl(ErrorCode(ex.Problem), returnUrl, ex.Problem.LockedUntil));
            }
        }).AllowAnonymous();

        group.MapPost("/logout", async (HttpContext http, IAuthApi auth, ISessionTokenProvider tokens, CancellationToken ct) =>
        {
            tokens.Override(http.User.FindFirstValue(GmsClaims.SessionToken));
            try
            {
                await auth.LogoutAsync(ct);
            }
            catch (ApiException)
            {
                // sesi sudah berakhir di server: tetap bersihkan cookie
            }

            await http.SignOutAsync(GmsClaims.Scheme);
            return Results.LocalRedirect($"{LoginPath}?info=signed-out");
        });

        // Dipakai saat API membalas 401 (sesi berakhir/digantikan/akun dinonaktifkan): bersihkan cookie, kembali ke login dengan pesan tepat.
        group.MapGet("/session-ended", async (HttpContext http, string? reason, string? returnUrl) =>
        {
            await http.SignOutAsync(GmsClaims.Scheme);
            var code = reason is "session-replaced" or "account-disabled" ? reason : "session-expired";
            return Results.LocalRedirect(LoginUrl(code, SafeReturnUrl(returnUrl)));
        }).AllowAnonymous();

        group.MapPost("/change-password", async ([FromForm] ChangePasswordForm form, HttpContext http, IAuthApi auth, ISessionTokenProvider tokens, CancellationToken ct) =>
        {
            var returnUrl = SafeReturnUrl(form.ReturnUrl);
            string Back(string error, string? message = null) =>
                $"{ChangePasswordPath}?error={error}&returnUrl={Uri.EscapeDataString(returnUrl)}{(message is null ? "" : "&message=" + Uri.EscapeDataString(message))}";

            if (string.IsNullOrEmpty(form.CurrentPassword) || string.IsNullOrEmpty(form.NewPassword))
            {
                return Results.LocalRedirect(Back("required"));
            }

            if (form.NewPassword != form.ConfirmPassword)
            {
                return Results.LocalRedirect(Back("mismatch"));
            }

            tokens.Override(http.User.FindFirstValue(GmsClaims.SessionToken));
            try
            {
                await auth.ChangePasswordAsync(new ChangePasswordRequest(form.CurrentPassword, form.NewPassword), ct);
                var me = await auth.GetMeAsync(ct); // klaim diperbarui: wajib-ganti-password sudah gugur
                await SignInAsync(http, me.User, http.User.FindFirstValue(GmsClaims.SessionToken)!, me.MustChangePassword, me.Permissions, 30);
                return Results.LocalRedirect(returnUrl);
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                return Results.LocalRedirect($"/account/session-ended?reason={ErrorCode(ex.Problem)}&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }
            catch (ApiException ex)
            {
                var message = ex.Problem.Errors?.Values.SelectMany(v => v).FirstOrDefault() ?? ex.Problem.Detail;
                return Results.LocalRedirect(Back("rejected", message is { Length: <= 300 } ? message : null));
            }
        }).RequireAuthorization();
    }

    public static async Task SignInAsync(
        HttpContext http, CurrentUserDto user, string token, bool mustChange, IEnumerable<string> permissions, int expiresInMinutes)
    {
        await http.SignInAsync(GmsClaims.Scheme, GmsClaims.Create(user, token, mustChange, permissions), new AuthenticationProperties
        {
            IsPersistent = false, AllowRefresh = true, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes),
        });
    }

    /// <summary>Hanya jalur lokal (diawali satu "/"); selain itu kembali ke beranda (cegah open redirect).</summary>
    public static string SafeReturnUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
        && !url.StartsWith(LoginPath, StringComparison.OrdinalIgnoreCase) && !url.Contains('\n') && !url.Contains('\r')
            ? url
            : "/";

    public static string ErrorCode(GmsProblemDetails problem) => problem.Type switch
    {
        ProblemTypes.InvalidCredentials => "invalid-credentials",
        ProblemTypes.AccountLocked => "account-locked",
        ProblemTypes.AccountDisabled => "account-disabled",
        ProblemTypes.SessionReplaced => "session-replaced",
        ProblemTypes.SessionExpired => "session-expired",
        ProblemTypes.ServiceUnavailable => "unavailable",
        _ => "error",
    };

    private static string LoginUrl(string error, string returnUrl, DateTimeOffset? until = null) =>
        $"{LoginPath}?error={error}&returnUrl={Uri.EscapeDataString(returnUrl)}{(until is { } u ? "&until=" + u.ToUnixTimeSeconds() : string.Empty)}";
}
