using Gms.Shared.Access;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IAuthApi"/> ke API sungguhan (<c>/api/v1/auth/*</c>).</summary>
public sealed class HttpAuthApi(GmsApiClient client) : IAuthApi
{
    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        client.PostAsync<LoginResponse>("/api/v1/auth/login", request, ct);

    public Task LogoutAsync(CancellationToken ct = default) => client.PostAsync("/api/v1/auth/logout", null, ct);

    public Task<MeResponse> GetMeAsync(CancellationToken ct = default) => client.GetAsync<MeResponse>("/api/v1/auth/me", ct);

    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default) =>
        client.PostAsync("/api/v1/auth/change-password", request, ct);
}

/// <summary>
/// Data tiruan untuk pengembangan UI tanpa API/DB (ADR-014). Perilaku galat meniru API: <see cref="ApiException"/> dengan Problem Details yang sama.
/// Akun demo: <c>admin</c> / <c>Admin123!</c> (semua izin), <c>staf</c> / <c>Staf123!</c> (hanya lihat PO dan Stok), <c>baru</c> / <c>Baru123!</c> (wajib ganti password).
/// </summary>
public sealed class MockAuthApi : IAuthApi
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (string Password, bool Must)> Passwords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = ("Admin123!", false), ["staf"] = ("Staf123!", false), ["baru"] = ("Baru123!", true),
    };

    private static readonly Dictionary<string, int> Failures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Sessions = [];

    private readonly Gms.Web.Infrastructure.Auth.ISessionTokenProvider _tokens;

    public MockAuthApi(Gms.Web.Infrastructure.Auth.ISessionTokenProvider tokens) => _tokens = tokens;

    private static CurrentUserDto UserOf(string name) => name.ToLowerInvariant() switch
    {
        "admin" => new CurrentUserDto(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "admin", "Administrator", Guid.Empty, "Administrator", true),
        "staf" => new CurrentUserDto(Guid.Parse("00000000-0000-0000-0000-0000000000b2"), "staf", "Staf Penjualan", Guid.Empty, "Staf Comfi", false),
        _ => new CurrentUserDto(Guid.Parse("00000000-0000-0000-0000-0000000000c3"), "baru", "Pengguna Baru", Guid.Empty, "Staf Comfi", false),
    };

    private static IReadOnlyList<string> PermissionsOf(string name) => name.ToLowerInvariant() switch
    {
        "admin" => PermissionCatalog.All.Select(p => p.Key).ToList(),
        _ => [Permissions.CustomerPO.View, Permissions.StokFG.View],
    };

    private static ApiException Problem(string type, int status, string title, string detail, DateTimeOffset? until = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, LockedUntil = until, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        lock (Gate)
        {
            var name = request.Username?.Trim() ?? string.Empty;
            if (Failures.GetValueOrDefault(name) >= 5)
            {
                throw Problem(ProblemTypes.AccountLocked, 401, "Akun terkunci.", "Terlalu banyak percobaan. Coba lagi nanti atau hubungi Administrator.", DateTimeOffset.UtcNow.AddMinutes(15));
            }

            if (!Passwords.TryGetValue(name, out var entry) || entry.Password != request.Password)
            {
                Failures[name] = Failures.GetValueOrDefault(name) + 1;
                throw Problem(ProblemTypes.InvalidCredentials, 401, "Login gagal.", "Username atau password salah.");
            }

            Failures.Remove(name);
            var token = Guid.NewGuid().ToString("N");
            Sessions[token] = name.ToLowerInvariant(); // satu sesi aktif per pengguna: sesi lama dicabut
            foreach (var stale in Sessions.Where(s => s.Value == name.ToLowerInvariant() && s.Key != token).Select(s => s.Key).ToList())
            {
                Sessions.Remove(stale);
            }

            return Task.FromResult(new LoginResponse(token, 30, UserOf(name), entry.Must, PermissionsOf(name)));
        }
    }

    private async Task<string> CurrentNameAsync()
    {
        var token = await _tokens.GetTokenAsync();
        lock (Gate)
        {
            if (token is not null && Sessions.TryGetValue(token, out var name))
            {
                return name;
            }
        }

        throw Problem(ProblemTypes.SessionExpired, 401, "Sesi berakhir.", "Silakan masuk kembali.");
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var token = await _tokens.GetTokenAsync();
        lock (Gate)
        {
            if (token is not null)
            {
                Sessions.Remove(token);
            }
        }
    }

    public async Task<MeResponse> GetMeAsync(CancellationToken ct = default)
    {
        var name = await CurrentNameAsync();
        return new MeResponse(UserOf(name), Passwords[name].Must, PermissionsOf(name));
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var name = await CurrentNameAsync();
        lock (Gate)
        {
            if (Passwords[name].Password != request.CurrentPassword)
            {
                throw Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.");
            }

            Passwords[name] = (request.NewPassword, false);
        }
    }
}
