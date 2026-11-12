using System.Security.Cryptography;
using System.Text;
using Gms.Api.Common;

namespace Gms.Api.Security;

/// <summary>
/// Kunci gerbang antara Web (BFF) dan API. Dipakai bila API terpaksa terjangkau publik (mis. layanan gratis PaaS tanpa jaringan privat):
/// permintaan tanpa header <c>X-Gms-Gateway-Key</c> yang benar ditolak 403 sebelum autentikasi (kecuali health check).
/// Kosong (bawaan) = nonaktif; di VPS/compose API tidak dipublikasikan sehingga tidak diperlukan. Nilai dari environment, bukan repo.
/// </summary>
public sealed class GatewayKeyMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const string HeaderName = "X-Gms-Gateway-Key";
    public const string ConfigKey = "Gateway:Key";
    private const string HealthPath = "/api/v1/health";

    private readonly byte[]? _expected = configuration[ConfigKey] is { Length: > 0 } key ? Hash(key) : null;

    public async Task InvokeAsync(HttpContext context)
    {
        if (_expected is null || context.Request.Path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var supplied = context.Request.Headers[HeaderName].ToString();
        if (supplied.Length == 0 || !CryptographicOperations.FixedTimeEquals(Hash(supplied), _expected))
        {
            await ProblemWriter.WriteAsync(context, ProblemWriter.ForStatus(StatusCodes.Status403Forbidden));
            return;
        }

        await next(context);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
