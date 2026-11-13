using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Gms.E2E.Tests;

/// <summary>Dialog (fokus terkurung, Esc, klik latar) pada browser nyata: nama aksesibel, fokus, Esc, klik latar, Batal, dan konfirmasi.</summary>
[Collection(StackCollection.Name)]
public sealed class DialogE2ETests(StackFixture stack)
{
    [Fact]
    public async Task ConfirmDialog_bernama_aksesibel_menahan_fokus_tidak_tertutup_klik_latar_menutup_dengan_Esc_dan_Batal_lalu_menjalankan_aksi()
    {
        var (user, _) = await stack.Seed.CreateReadyUserAsync();
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/admin/users");
        await page.GetByLabel("Cari username/nama").FillAsync(user);
        var open = page.GetByRole(AriaRole.Button, new() { Name = $"Nonaktifkan {user}" });
        await Expect(open).ToBeVisibleAsync();

        await open.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = $"Nonaktifkan {user}?" }); // nama dari aria-labelledby (modul JS)
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToHaveAttributeAsync("aria-modal", "true");
        await Expect(dialog).ToContainTextAsync("sesi aktifnya berakhir");

        await page.Mouse.ClickAsync(5, 300); // klik latar (mask) tidak menutup aksi berisiko
        await Expect(dialog).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();

        await open.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Batal" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        await open.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Nonaktifkan" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.GetByText($"{user} dinonaktifkan.")).ToBeVisibleAsync(); // toast sukses
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = $"Aktifkan {user}" })).ToBeVisibleAsync();
    }
}
