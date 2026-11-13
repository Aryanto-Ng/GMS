using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Gms.E2E.Tests;

/// <summary>Notifikasi pada tumpukan nyata: peristiwa bisnis (WO dirilis oleh pengguna lain) → lencana lonceng, panel, buka dokumen di tab, Semua notifikasi, dan Pengaturan.</summary>
[Collection(StackCollection.Name)]
public sealed class NotificationE2ETests(StackFixture stack)
{
    [Fact]
    public async Task WO_dirilis_pengguna_lain_muncul_di_lonceng_dibuka_di_tab_dan_tercatat_di_Semua_notifikasi()
    {
        // Pengguna UI harus ada SEBELUM peristiwa (penerima ditetapkan saat notifikasi dibuat); pelaku = Administrator bootstrap (API).
        var page = await stack.LoginAsAdminAsync();
        var data = await stack.Seed.CreateBaseDataAsync();
        var po = await stack.Seed.CreateConfirmedPoAsync(data, "PO/NOTIF/1");
        var wo = await stack.Seed.CreateReleasedWoAsync(po);

        await StackFixture.GotoInteractiveAsync(page, "/");
        var bell = page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Notifikasi") });
        await Expect(bell).ToHaveAttributeAsync("aria-label", new Regex(@"^Notifikasi, \d+ belum dibaca$"));
        await Expect(bell.Locator(".gms-badge-dot")).ToBeVisibleAsync();

        await bell.ClickAsync();
        var panel = page.GetByRole(AriaRole.Dialog, new() { Name = "Notifikasi" });
        await Expect(panel).ToBeVisibleAsync();
        var item = panel.GetByRole(AriaRole.Listitem).Filter(new() { HasText = "dirilis" }).First;
        await Expect(item).ToBeVisibleAsync();
        await Expect(item).ToContainTextAsync("Belum dibaca");

        await item.ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex($"/work-orders/{wo.Id}$"));
        await Expect(page.GetByRole(AriaRole.Tab).Filter(new() { HasText = "WO-" })).ToHaveCountAsync(1); // dibuka sebagai tab dokumen

        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Notifikasi") }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Lihat semua notifikasi" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/notifications$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Semua notifikasi" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Table).GetByText("dirilis").First).ToBeVisibleAsync();
        await Expect(page.GetByText("Notifikasi disimpan selama 90 hari.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Pengaturan_Notifikasi_dibuka_Administrator_diubah_disimpan_dan_dimuat_ulang_dari_server()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/admin/notifications");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Pengaturan Notifikasi" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Checkbox)).ToHaveCountAsync(16);

        var due = page.GetByLabel("Ambang jatuh tempo (hari)");
        await due.FillAsync("5");
        await Expect(page.Locator(".gms-tab.on .gms-dirty")).ToHaveCountAsync(1);
        await page.GetByRole(AriaRole.Checkbox, new() { Name = "Aktif Stok tersedia habis" }).UncheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Expect(page.GetByText("Pengaturan notifikasi disimpan.")).ToBeVisibleAsync();
        await Expect(page.Locator(".gms-tab.on .gms-dirty")).ToHaveCountAsync(0);

        await StackFixture.GotoInteractiveAsync(page, "/admin/notifications"); // muat ulang penuh dari API
        await Expect(page.GetByLabel("Ambang jatuh tempo (hari)")).ToHaveValueAsync("5");
        await Expect(page.GetByRole(AriaRole.Checkbox, new() { Name = "Aktif Stok tersedia habis" })).Not.ToBeCheckedAsync();

        // Nilai di luar rentang ditolak sebelum dikirim.
        await page.GetByLabel("Masa simpan (hari)").FillAsync("9");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Expect(page.GetByText("Masa simpan harus 30 sampai 365 hari.").First).ToBeVisibleAsync();
    }
}
