using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Gms.E2E.Tests;

/// <summary>Shell Fluent 2 (Design.md 3 sampai 6) pada tumpukan nyata: Top Navigation, pencarian header, Tab Manager (state per tab), tema terang saja.</summary>
[Collection(StackCollection.Name)]
public sealed class ShellE2ETests(StackFixture stack)
{
    [Fact]
    public async Task Top_navigation_dibuka_dengan_klik_menampilkan_halaman_dan_aksi_lalu_menavigasi_membuka_tab_dan_menutup()
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
        await Expect(page.GetByRole(AriaRole.Tab)).ToHaveCountAsync(2); // Beranda (terpaku) + Daftar WO
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Work Order" })).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task Pencarian_header_menyaring_menu_menyebut_tag_aksi_membuka_dengan_Enter_dan_menutup_dengan_Esc()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/");
        var search = page.GetByRole(AriaRole.Searchbox, new() { Name = "Cari halaman atau aksi" });

        await search.FillAsync("buat po");
        var results = page.GetByRole(AriaRole.Listbox, new() { Name = "Hasil pencarian" }).GetByRole(AriaRole.Option);
        await Expect(results.First).ToContainTextAsync("Buat PO");
        await Expect(results.First).ToContainTextAsync("Aksi");
        Assert.True(await results.CountAsync() <= 7);

        await search.PressAsync("Enter");
        await Expect(page).ToHaveURLAsync(new Regex("/purchase-orders/new$"));

        await search.FillAsync("zzzxyz");
        await Expect(page.GetByText("Tidak ada hasil untuk pencarian ini")).ToBeVisibleAsync();
        await search.PressAsync("Escape");
        await Expect(page.GetByText("Tidak ada hasil untuk pencarian ini")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Tab_manager_menjaga_isi_form_per_tab_menandai_belum_disimpan_dan_menutup_dengan_konfirmasi()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/purchase-orders/new");

        var notes = page.GetByLabel("Keterangan");
        await notes.FillAsync("catatan yang tidak boleh hilang");
        await Expect(page.Locator(".gms-tab.on .gms-dirty")).ToHaveCountAsync(1); // titik oranye: belum disimpan

        // Pindah ke tab Beranda lalu kembali: isi form tetap ada (halaman tab tidak dibuat ulang).
        await page.GetByRole(AriaRole.Tab, new() { Name = "Beranda" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }).First).ToContainTextAsync("Selamat datang");
        await page.GetByRole(AriaRole.Tab, new() { Name = "Buat PO" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/purchase-orders/new$"));
        await Expect(notes).ToHaveValueAsync("catatan yang tidak boleh hilang");

        // Menutup tab yang belum disimpan meminta konfirmasi; "Tetap di sini" mempertahankannya.
        await page.GetByRole(AriaRole.Button, new() { Name = "Tutup Buat PO" }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Tutup tab tanpa menyimpan?" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Tetap di sini" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Buat PO" })).ToHaveCountAsync(1);

        await page.GetByRole(AriaRole.Button, new() { Name = "Tutup Buat PO" }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Tutup tanpa menyimpan" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Buat PO" })).ToHaveCountAsync(0);
        await Expect(page).ToHaveURLAsync(new Regex("/$")); // kembali ke tab tetangga (Beranda)
    }

    [Fact]
    public async Task Tema_hanya_terang_tanpa_pengalih_tema_dan_header_berlatar_brand()
    {
        var page = await stack.LoginAsAdminAsync();
        await StackFixture.GotoInteractiveAsync(page, "/");

        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Tema" }).CountAsync());
        Assert.Null(await page.Locator("html").GetAttributeAsync("data-theme"));
        var header = await page.EvaluateAsync<string>("getComputedStyle(document.querySelector('.gms-hdr')).backgroundColor");
        Assert.Equal("rgb(15, 108, 189)", header); // token --brand #0F6CBD
        var canvas = await page.EvaluateAsync<string>("getComputedStyle(document.querySelector('.gms-main')).backgroundColor");
        Assert.Equal("rgb(245, 245, 245)", canvas); // token --bg3
    }
}
