using System.Security.Cryptography;

namespace Gms.Web.Infrastructure;

/// <summary>
/// SEC-002: header keamanan pada setiap respons. CSP memperbolehkan hanya sumber sendiri; <c>style-src 'unsafe-inline'</c> diperlukan karena
/// AntDesign dan Blazor menulis atribut style, <c>connect-src ws: wss:</c> untuk sirkuit SignalR Blazor Server. Satu-satunya skrip inline
/// (import map buatan framework) diizinkan lewat nonce per permintaan, bukan <c>'unsafe-inline'</c>.
/// </summary>
public static class SecurityHeaders
{
    public const string NonceItemKey = "gms.csp.nonce";

    public static string BuildPolicy(string nonce) =>
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; "
        + "connect-src 'self' ws: wss:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceItemKey] = nonce;
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = BuildPolicy(nonce);
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                return Task.CompletedTask;
            });
            await next();
        });
}
