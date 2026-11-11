using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Gms.E2E.Tests;

/// <summary>Perbaikan audit keamanan: header (SEC-002) dan pembatasan login per IP (SEC-004) pada tumpukan nyata.</summary>
[Collection(StackCollection.Name)]
public sealed class SecurityE2ETests(StackFixture stack)
{
    [Fact]
    public async Task Respons_membawa_header_keamanan_dan_aplikasi_berjalan_tanpa_pelanggaran_CSP()
    {
        var page = await stack.NewPageAsync();
        var violations = new List<string>();
        page.Console += (_, m) => { if (m.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase) || m.Text.Contains("Refused to", StringComparison.OrdinalIgnoreCase)) { violations.Add(m.Text); } };

        var response = await page.GotoAsync("/login");
        var headers = response!.Headers;
        Assert.Contains("frame-ancestors 'none'", headers["content-security-policy"]);
        Assert.Matches(@"script-src 'self' 'nonce-[A-Za-z0-9+/=]+'", headers["content-security-policy"]);
        Assert.DoesNotContain("unsafe-eval", headers["content-security-policy"]);
        Assert.DoesNotContain("unsafe-eval", headers["content-security-policy"]);
        Assert.Equal("nosniff", headers["x-content-type-options"]);
        Assert.Equal("DENY", headers["x-frame-options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["referrer-policy"]);

        // Alur interaktif nyata: login, Blazor Server (WebSocket), MegaMenu, palette, tema, dialog.
        var (user, password) = await stack.Seed.CreateReadyUserAsync();
        await StackFixture.LoginAsync(page, user, password);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Selamat datang");
        await StackFixture.GotoInteractiveAsync(page, "/work-orders");
        await page.GetByRole(AriaRole.Button, new() { Name = "Produksi" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Region, new() { Name = "Produksi" })).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.Keyboard.PressAsync("Control+k");
        await Expect(page.GetByRole(AriaRole.Combobox, new() { Name = "Cari menu" })).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.GetByRole(AriaRole.Button, new() { Name = "Tema" }).ClickAsync();

        Assert.Empty(violations);
    }

    [Fact]
    public async Task Halaman_tidak_dapat_dibingkai_frame_ancestors_none()
    {
        var page = await stack.NewPageAsync();
        await page.GotoAsync("/login");
        var blocked = await page.EvaluateAsync<bool>(
            "async (url) => { const f = document.createElement('iframe'); f.src = url; document.body.appendChild(f); await new Promise(r => setTimeout(r, 1500)); try { return f.contentDocument === null || f.contentDocument.body === null || f.contentDocument.body.innerHTML === ''; } catch { return true; } }",
            stack.WebUrl + "/login");
        Assert.True(blocked, "halaman login tidak boleh tampil di dalam iframe");
    }

    [Fact]
    public async Task Login_dibatasi_per_IP_percobaan_berlebih_dialihkan_dengan_pesan()
    {
        var url = await stack.StartRateLimitedWebAsync(loginPerMinute: 3);
        var context = await (await stack.NewPageAsync()).Context.Browser!.NewContextAsync(new() { BaseURL = url, Locale = "id-ID" });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);

        for (var i = 0; i < 3; i++)
        {
            await StackFixture.LoginAsync(page, "tidak.ada", "Salah12345");
            await Expect(page.Locator("[role=alert], .gms-banner")).ToContainTextAsync("Login gagal");
        }

        await StackFixture.LoginAsync(page, "tidak.ada", "Salah12345");
        await Expect(page).ToHaveURLAsync(new Regex("error=too-many-requests"));
        await Expect(page.Locator("[role=alert], .gms-banner")).ToContainTextAsync("Terlalu banyak percobaan");
    }
}
