using Microsoft.Playwright;

namespace Gms.E2E.Tests;

/// <summary>
/// Tangkapan layar untuk peninjauan visual (bukan pengujian): aktif hanya bila variabel lingkungan <c>GMS_SHOTS_DIR</c> diisi, mis.
/// <c>GMS_SHOTS_DIR=/tmp/shots dotnet test ... --filter-class Gms.E2E.Tests.ScreenshotE2ETests</c>. Tanpa variabel itu tes selesai tanpa melakukan apa pun.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class ScreenshotE2ETests(StackFixture stack)
{
    [Fact]
    public async Task Tangkap_layar_halaman_utama_bila_GMS_SHOTS_DIR_diisi()
    {
        var dir = Environment.GetEnvironmentVariable("GMS_SHOTS_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var data = await stack.Seed.CreateBaseDataAsync();
        var po = await stack.Seed.CreateConfirmedPoAsync(data, "PO/SHOT/1");
        var wo = await stack.Seed.CreateReleasedWoAsync(po);

        var anon = await stack.NewPageAsync();
        await anon.GotoAsync("/login?error=invalid-credentials");
        await anon.ScreenshotAsync(new() { Path = Path.Combine(dir, "login.png") });

        var page = await stack.LoginAsAdminAsync();
        async Task Shot(string name, string path, Func<Task>? before = null)
        {
            await StackFixture.GotoInteractiveAsync(page, path);
            await page.WaitForFunctionAsync("() => document.querySelectorAll('[aria-busy=\"true\"]').length === 0", null, new() { Timeout = 20_000 });
            await Task.Delay(400);
            if (before is not null)
            {
                await before();
            }

            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, name + ".png") });
        }

        await Shot("home", "/");
        await Shot("po-list", "/purchase-orders");
        await Shot("po-detail", $"/purchase-orders/{po.Id}");
        await Shot("po-new", "/purchase-orders/new");
        await Shot("wo-detail", $"/work-orders/{wo.Id}");
        await Shot("output", "/production/output");
        await Shot("fg-detail", $"/master/fgs/{data.FgId}");
        await Shot("roles", "/admin/roles");
        await Shot("menu", "/", async () => await page.GetByRole(AriaRole.Button, new() { Name = "Produksi" }).ClickAsync());
    }
}
