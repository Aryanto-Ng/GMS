using Gms.Api.Hosting;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Api.Tests.Infrastructure;

public sealed record TestUser(Guid Id, string UserName, string Password, string Token, Guid RoleId);

/// <summary>
/// Satu instance API sungguhan (ApiHost.Build) pada port acak di atas satu database PostgreSQL nyata,
/// sudah dimigrasi dan di-seed. Mengikuti jalur yang sama dengan produksi, termasuk autentikasi sesi.
/// </summary>
public sealed class TestHost : IAsyncDisposable
{
    public const string BootstrapPassword = "Bootstrap123";
    public const string BootstrapFinalPassword = "Bootstrap456";
    public const string DefaultPassword = "Awal12345";

    private readonly WebApplication _app;
    private readonly HttpClient _http;

    private TestHost(WebApplication app, HttpClient http, string connectionString)
    {
        _app = app;
        _http = http;
        ConnectionString = connectionString;
        Api = new ApiClient(http);
    }

    public string ConnectionString { get; }
    public ApiClient Api { get; }
    public IServiceProvider Services => _app.Services;

    /// <summary>Token Administrator bootstrap (sesi tidak pernah di-login ulang oleh tes agar tetap valid).</summary>
    public string AdminToken { get; private set; } = string.Empty;

    public Guid AdminRoleId { get; private set; }

    public static async Task<TestHost> StartAsync(string connectionString, IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        await using (var db = CreateDbContext(connectionString))
        {
            await db.Database.MigrateAsync();
        }

        var app = ApiHost.Build(["--urls", "http://127.0.0.1:0"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connectionString,
                ["Seed:AdminUserName"] = "admin",
                ["Seed:AdminPassword"] = BootstrapPassword,
                ["Logging:LogLevel:Default"] = "Warning",
                ["Notifications:SchedulerEnabled"] = "false",
            });

            if (extraConfig is not null)
            {
                builder.Configuration.AddInMemoryCollection(extraConfig);
            }
        });

        using (var scope = app.Services.CreateScope())
        {
            await DatabaseInitializer.SeedAsync(scope.ServiceProvider);
        }

        await app.StartAsync();
        var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var host = new TestHost(app, http, connectionString);
        await host.BootstrapAsync();
        return host;
    }

    public static GmsDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GmsDbContext>().UseNpgsql(connectionString).Options);

    public GmsDbContext CreateDbContext() => CreateDbContext(ConnectionString);

    private async Task BootstrapAsync()
    {
        var first = (await Api.PostAsync("/api/v1/auth/login", new LoginRequest("admin", BootstrapPassword))).Ok("login bootstrap").As<LoginResponse>();
        (await Api.PostAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(BootstrapPassword, BootstrapFinalPassword), first.Token)).Ok("ganti password bootstrap");
        var second = (await Api.PostAsync("/api/v1/auth/login", new LoginRequest("admin", BootstrapFinalPassword))).Ok().As<LoginResponse>();
        AdminToken = second.Token;
        AdminRoleId = second.User.RoleId;
    }

    /// <summary>Login ulang Administrator bootstrap (mencabut sesi lamanya). Dipakai tes yang sengaja men-login-nya.</summary>
    public async Task RefreshAdminTokenAsync() =>
        AdminToken = (await LoginAsync("admin", BootstrapFinalPassword)).Token;

    // ===== Helper skenario =====

    public static string UniqueName(string prefix = "u") => prefix + Guid.NewGuid().ToString("N")[..10];

    public async Task<RoleDetail> CreateRoleAsync(string? name = null, params string[] permissionKeys)
    {
        var response = await Api.PostAsync("/api/v1/roles",
            new SaveRoleRequest(name ?? UniqueName("Role"), "Role uji", permissionKeys), AdminToken);
        return response.Ok("buat role").As<RoleDetail>();
    }

    /// <summary>Membuat pengguna oleh Administrator bootstrap lalu mengembalikan akun yang belum pernah login.</summary>
    public async Task<UserDetail> CreateUserAsync(Guid roleId, string? userName = null, string password = DefaultPassword)
    {
        var response = await Api.PostAsync("/api/v1/users",
            new CreateUserRequest(userName ?? UniqueName(), "Pengguna Uji", roleId, password), AdminToken);
        return response.Ok("buat pengguna").As<UserDetail>();
    }

    public async Task<LoginResponse> LoginAsync(string userName, string password) =>
        (await Api.PostAsync("/api/v1/auth/login", new LoginRequest(userName, password))).Ok($"login {userName}").As<LoginResponse>();

    /// <summary>Membuat pengguna, login pertama, mengganti password awal, lalu login lagi: akun siap pakai.</summary>
    public async Task<TestUser> CreateReadyUserAsync(Guid roleId, string? userName = null)
    {
        var detail = await CreateUserAsync(roleId, userName);
        var firstLogin = await LoginAsync(detail.UserName, DefaultPassword);
        const string finalPassword = "Final12345";
        (await Api.PostAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(DefaultPassword, finalPassword), firstLogin.Token)).Ok("ganti password awal");
        var ready = await LoginAsync(detail.UserName, finalPassword);
        return new TestUser(detail.Id, detail.UserName, finalPassword, ready.Token, roleId);
    }

    /// <summary>Administrator tambahan yang siap pakai (sesi sendiri, tidak mengganggu sesi bootstrap).</summary>
    public Task<TestUser> CreateReadyAdminAsync() => CreateReadyUserAsync(AdminRoleId);

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
