using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace Gms.E2E.Tests;

/// <summary>
/// Tumpukan nyata untuk E2E: PostgreSQL 17 (Testcontainers) + Gms.Api + Gms.Web (UseMock=false) sebagai proses terpisah + Chromium (Playwright).
/// Log proses ditulis ke <c>logs/</c> di folder keluaran untuk diagnosis.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    public const string BootstrapPassword = "Awal-12345";
    public const string AdminPassword = "Admin-67890";

    private PostgreSqlContainer _db = null!;
    private readonly List<Process> _processes = [];
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public string ApiUrl { get; private set; } = string.Empty;

    public string WebUrl { get; private set; } = string.Empty;

    /// <summary>Klien API untuk menyiapkan data (bukan lewat UI) dan memverifikasi hasil.</summary>
    public ApiSeeder Seed { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _db = new PostgreSqlBuilder("postgres:17").Build();
        await _db.StartAsync();

        var apiPort = FreePort();
        var webPort = FreePort();
        ApiUrl = $"http://127.0.0.1:{apiPort}";
        WebUrl = $"http://127.0.0.1:{webPort}";

        var logs = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logs);

        StartProcess("api", Path.Combine(FindOutput("Backend", "Gms.Api"), "Gms.Api.dll"), logs, new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = ApiUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ConnectionStrings__Default"] = _db.GetConnectionString(),
            ["Database__MigrateOnStartup"] = "true",
            ["Database__SeedOnStartup"] = "true",
            ["Seed__AdminUserName"] = "admin",
            ["Seed__AdminPassword"] = BootstrapPassword,
            ["App__TimeZone"] = "Asia/Jakarta",
            ["Logging__LogLevel__Default"] = "Warning",
        });
        await WaitAsync($"{ApiUrl}/api/v1/auth/me", expected: HttpStatusCode.Unauthorized, "API");

        StartProcess("web", Path.Combine(FindOutput("Frontend", "Gms.Web"), "Gms.Web.dll"), logs, new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = WebUrl,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Api__BaseUrl"] = ApiUrl,
            ["Api__UseMock"] = "false",
            ["RateLimit__LoginPerMinute"] = "10000", // E2E melakukan banyak login dari satu IP
            ["Logging__LogLevel__Default"] = "Warning",
        });
        await WaitAsync($"{WebUrl}/login", expected: HttpStatusCode.OK, "Web");

        Seed = await ApiSeeder.CreateAsync(ApiUrl, BootstrapPassword, AdminPassword);

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>Konteks browser baru (cookie terisolasi) dan halaman; layar desktop standar.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = 1366, Height = 900 }, Locale = "id-ID", BaseURL = WebUrl });
        context.SetDefaultTimeout(15_000);
        return await context.NewPageAsync();
    }

    /// <summary>Login (lewat form sungguhan) sebagai pengguna Administrator baru agar sesi tidak saling menggantikan antar tes.</summary>
    public async Task<IPage> LoginAsAdminAsync()
    {
        var (user, password) = await Seed.CreateReadyUserAsync();
        var page = await NewPageAsync();
        await LoginAsync(page, user, password);
        await Microsoft.Playwright.Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Selamat datang");
        return page;
    }

    /// <summary>Membuka halaman dan menunggu sirkuit Blazor Server (WebSocket) tersambung sebelum berinteraksi, agar klik/isian tidak hilang pada halaman yang baru di-prerender.</summary>
    public static async Task GotoInteractiveAsync(IPage page, string path)
    {
        await page.RunAndWaitForWebSocketAsync(async () => await page.GotoAsync(path));
        await Task.Delay(700);
    }

    public static async Task LoginAsync(IPage page, string user, string password)
    {
        await page.GotoAsync("/login");
        await page.GetByLabel("Nama pengguna").FillAsync(user);
        await page.GetByLabel("Kata sandi").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Masuk" }).ClickAsync();
    }

    /// <summary>Menjalankan Web kedua (API yang sama) dengan batas login per IP yang rendah untuk menguji pembatasan; mengembalikan URL-nya.</summary>
    public async Task<string> StartRateLimitedWebAsync(int loginPerMinute)
    {
        var url = $"http://127.0.0.1:{FreePort()}";
        var logs = Path.Combine(AppContext.BaseDirectory, "logs");
        StartProcess("web-ratelimit", Path.Combine(FindOutput("Frontend", "Gms.Web"), "Gms.Web.dll"), logs, new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = url,
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Api__BaseUrl"] = ApiUrl,
            ["Api__UseMock"] = "false",
            ["RateLimit__LoginPerMinute"] = loginPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Logging__LogLevel__Default"] = "Warning",
        });
        await WaitAsync($"{url}/login", expected: HttpStatusCode.OK, "Web");
        return url;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindOutput(string group, string project)
    {
        // Tata letak artifacts (-p:UseArtifactsOutput=true): .../bin/Gms.E2E.Tests/debug/ -> .../bin/<project>/debug/. Dipakai bila ada (mis. saat proses lain mengunci bin/ biasa).
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var sibling = baseDir.Parent is { } binGroup ? Path.Combine(binGroup.Parent!.FullName, project, baseDir.Name) : null;
        if (sibling is not null && File.Exists(Path.Combine(sibling, project + ".dll")))
        {
            return sibling;
        }

        // .../Tests/Gms.E2E.Tests/bin/Debug/net10.0/ → .../<group>/<project>/bin/Debug/net10.0/
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Gms.slnx")))
        {
            root = root.Parent;
        }

        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, group, project, "bin", configuration, "net10.0");
    }

    private void StartProcess(string name, string dll, string logs, Dictionary<string, string> env)
    {
        var info = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var (key, value) in env) { info.Environment[key] = value; }
        var process = Process.Start(info) ?? throw new InvalidOperationException($"Gagal menjalankan {name}.");
        var log = new StreamWriter(Path.Combine(logs, $"{name}.log")) { AutoFlush = true };
        process.OutputDataReceived += (_, e) => log.WriteLine(e.Data);
        process.ErrorDataReceived += (_, e) => log.WriteLine(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
    }

    private static async Task WaitAsync(string url, HttpStatusCode expected, string name)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(url);
                if (response.StatusCode == expected) { return; }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(500);
        }

        throw new TimeoutException($"{name} tidak siap di {url} dalam 90 detik. Lihat logs/{name.ToLowerInvariant()}.log.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) { await _browser.CloseAsync(); }
        _playwright?.Dispose();
        foreach (var p in _processes)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            p.Dispose();
        }

        if (_db is not null) { await _db.DisposeAsync(); }
    }
}

[CollectionDefinition(Name)]
public sealed class StackCollection : ICollectionFixture<StackFixture>
{
    public const string Name = "stack";
}
