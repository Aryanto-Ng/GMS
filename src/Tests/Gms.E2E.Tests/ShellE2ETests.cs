using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Gms.E2E.Tests;

/// <summary>Shell (Design.md 5 dan 7) pada tumpukan nyata: MegaMenu, CommandPalette (JS), dan tema terang/gelap yang bertahan.</summary>
[Collection(StackCollection.Name)]
public sealed class ShellE2ETests(StackFixture stack)
{
    [Fact]
    public async Task MegaMenu_dibuka_dengan_klik_menampilkan_halaman_dan_aksi_lalu_menavigasi_dan_menutup()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/");

        await page.GetByRole(AriaRole.Button, new() { Name = "Produksi" }).ClickAsync();

        var panel = page.GetByRole(AriaRole.Region, new() { Name = "Produksi" });
        await Expect(panel).ToBeVisibleAsync();
        await Expect(panel.GetByRole(AriaRole.Link, new() { Name = "Daftar WO" })).ToBeVisibleAsync();
        await Expect(panel.GetByRole(AriaRole.Link, new() { Name = "Buat WO" })).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToHaveCountAsync(0);

        await page.GetByRole(AriaRole.Button, new() { Name = "Produksi" }).ClickAsync();
        await page.GetByRole(AriaRole.Region, new() { Name = "Produksi" }).GetByRole(AriaRole.Link, new() { Name = "Daftar WO" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/work-orders$"));
        await Expect(page.GetByRole(AriaRole.Region, new() { Name = "Produksi" })).ToHaveCountAsync(0); // panel menutup setelah navigasi
    }

    [Fact]
    public async Task CommandPalette_Ctrl_K_menyaring_di_browser_membuka_tujuan_dengan_Enter_dan_menutup_dengan_Esc()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/");

        await page.Keyboard.PressAsync("Control+k");

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Cari menu" });
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.GetByRole(AriaRole.Combobox)).ToBeFocusedAsync();
        await Expect(dialog.GetByText("Aksi cepat")).ToBeVisibleAsync(); // query kosong: aksi cepat

        await page.Keyboard.TypeAsync("buat po");
        await Expect(dialog.GetByRole(AriaRole.Option).First).ToContainTextAsync("Buat PO");
        await Expect(dialog.GetByRole(AriaRole.Option)).ToHaveCountAsync(2); // Buat PO dan Buat WO ("po" cocok pada deskripsi)

        await page.Keyboard.PressAsync("Enter");
        await Expect(page).ToHaveURLAsync(new Regex("/purchase-orders/new$"));
        await Expect(dialog).ToBeHiddenAsync();

        await page.Keyboard.PressAsync("Control+k");
        await page.Keyboard.TypeAsync("zzzxyz");
        await Expect(dialog.GetByText("Tidak ada menu yang cocok")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Pengalih_tema_mengubah_data_theme_dan_pilihan_bertahan_setelah_muat_ulang()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/");
        var toggle = page.Locator("button.gms-iconbtn");

        await toggle.ClickAsync(); // sistem -> terang
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await toggle.ClickAsync(); // terang -> gelap
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");

        await page.ReloadAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        var background = await page.EvaluateAsync<string>("getComputedStyle(document.body).backgroundColor");
        Assert.Equal("rgb(0, 0, 0)", background); // token bg-layout gelap
    }

    [Fact]
    public async Task CommandPalette_menjaga_fokus_dan_tetap_menutup_dengan_Esc_walau_router_memindahkan_fokus_ke_judul()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/");
        await page.Keyboard.PressAsync("Control+k");
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Cari menu" });
        await Expect(dialog).ToBeVisibleAsync();

        // Meniru FocusOnNavigate: fokus dipindah ke <h1> saat palette terbuka (terjadi pada CI yang lebih lambat setelah navigasi).
        await page.EvaluateAsync("() => { const h = document.querySelector('h1'); h.setAttribute('tabindex', '-1'); h.focus(); }");
        await Expect(dialog.GetByRole(AriaRole.Combobox)).ToBeFocusedAsync(); // fokus dikembalikan ke kotak pencarian

        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    }
}
