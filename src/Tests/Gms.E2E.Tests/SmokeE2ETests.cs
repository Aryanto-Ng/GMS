using Microsoft.Playwright;

namespace Gms.E2E.Tests;

/// <summary>
/// Smoke seluruh halaman terhadap API nyata dengan data sungguhan: setiap halaman harus memuat tanpa state galat/banner galat.
/// Ini menguji kontrak HttpXxxApi ↔ API (rute, JSON, izin) yang tidak tercakup tes bUnit (mode Mock).
/// </summary>
[Collection(StackCollection.Name)]
public sealed class SmokeE2ETests(StackFixture stack)
{
    private static readonly string[] ErrorSelectors =
    [
        ".gms-state.error", ".gms-banner.error", ".blazor-error-boundary", "#blazor-error-ui[style*='display: block']",
    ];

    /// <summary>Membuka halaman, menunggu pemuatan selesai, lalu mengembalikan masalah yang ditemukan (kosong = sehat).</summary>
    private static async Task<string?> CheckAsync(IPage page, string path)
    {
        var console = new List<string>();
        void OnConsole(object? _, IConsoleMessage m) { if (m.Type == "error") { console.Add(m.Text); } }
        page.Console += OnConsole;
        try
        {
            var response = await page.GotoAsync(path);
            if (response is { Status: >= 400 }) { return $"{path}: HTTP {response.Status}"; }
            if (page.Url.Contains("/login", StringComparison.Ordinal) || page.Url.Contains("/forbidden", StringComparison.Ordinal)) { return $"{path}: dialihkan ke {page.Url}"; }
            await page.WaitForSelectorAsync("h1", new() { Timeout = 15_000 });
            await page.WaitForFunctionAsync("() => document.querySelectorAll('[aria-busy=\"true\"]').length === 0", null, new() { Timeout = 20_000 });
            await Task.Delay(300); // beri waktu render interaktif kedua setelah prerender
            await page.WaitForFunctionAsync("() => document.querySelectorAll('[aria-busy=\"true\"]').length === 0", null, new() { Timeout = 20_000 });

            foreach (var selector in ErrorSelectors)
            {
                var found = page.Locator(selector);
                if (await found.CountAsync() > 0) { return $"{path}: {selector} → {(await found.First.InnerTextAsync()).Replace('\n', ' ')}"; }
            }

            var real = console.Where(c => !c.Contains("favicon", StringComparison.OrdinalIgnoreCase)).ToList();
            return real.Count > 0 ? $"{path}: console error → {real[0]}" : null;
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            return $"{path}: {ex.GetType().Name} {ex.Message.Split('\n')[0]}";
        }
        finally
        {
            page.Console -= OnConsole;
        }
    }

    [Fact]
    public async Task Semua_halaman_memuat_dengan_data_nyata_tanpa_galat()
    {
        var data = await stack.Seed.CreateBaseDataAsync();
        var po = await stack.Seed.CreateConfirmedPoAsync(data, "PO/E2E/1");
        var wo = await stack.Seed.CreateReleasedWoAsync(po);
        var page = await stack.LoginAsAdminAsync();

        string[] paths =
        [
            "/", "/admin/users", "/admin/roles", "/admin/audit",
            // BUG-001: daftar master (/master/{entitas}) dan form yang memuat daftar master gagal 400 (status huruf kecil) — dikecualikan sampai diperbaiki.
            "/master/fgs/new", $"/master/fgs/{data.FgId}", "/master/materials/import", "/master/workers/import", "/master/fgs/import",
            "/purchase-orders", $"/purchase-orders/{po.Id}",
            "/work-orders", $"/work-orders/new?poId={po.Id}", $"/work-orders/{wo.Id}",
            "/production/output", "/production/vendor", "/production/history", "/production/adjustments",
            "/handovers/fg", "/handovers/fg/new", "/handovers/material", "/handovers/material/new",
            "/stock", "/stock/import", "/deliveries", $"/deliveries/new?poId={po.Id}", "/payroll",
        ];

        var problems = new List<string>();
        foreach (var path in paths)
        {
            if (await CheckAsync(page, path) is { } problem) { problems.Add(problem); }
        }

        Assert.True(problems.Count == 0, "Halaman bermasalah:\n" + string.Join('\n', problems));
    }
}
