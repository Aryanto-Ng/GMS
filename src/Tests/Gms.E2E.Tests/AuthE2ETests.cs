using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Gms.E2E.Tests;

/// <summary>Alur autentikasi pada tumpukan nyata (cookie BFF + token sesi API): login, ganti password wajib, otorisasi, logout.</summary>
[Collection(StackCollection.Name)]
public sealed class AuthE2ETests(StackFixture stack)
{
    [Fact]
    public async Task Password_salah_menampilkan_pesan_generik_di_atas_form()
    {
        var page = await stack.NewPageAsync();

        await StackFixture.LoginAsync(page, "admin", "salah-banget-1");

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Username atau password salah.");
        await Expect(page).ToHaveURLAsync(new Regex("/login"));
    }

    [Fact]
    public async Task Login_pertama_memaksa_ganti_password_lalu_masuk_ke_Beranda()
    {
        var (user, password) = await stack.Seed.CreateUserAsync();
        var page = await stack.NewPageAsync();

        await StackFixture.LoginAsync(page, user, password);

        await Expect(page).ToHaveURLAsync(new Regex("change-password"));
        await Expect(page.Locator(".gms-banner.info")).ToContainTextAsync("harus mengganti password");
        await page.Locator("#current").FillAsync(password);
        await page.Locator("#new").FillAsync("Baru-98765");
        await page.Locator("#confirm").FillAsync("Baru-98765");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan password" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Selamat datang");
    }

    [Fact]
    public async Task Ganti_password_dengan_konfirmasi_berbeda_ditolak_dengan_pesan()
    {
        var (user, password) = await stack.Seed.CreateUserAsync();
        var page = await stack.NewPageAsync();
        await StackFixture.LoginAsync(page, user, password);
        await Expect(page).ToHaveURLAsync(new Regex("change-password"));

        await page.Locator("#current").FillAsync(password);
        await page.Locator("#new").FillAsync("Baru-98765");
        await page.Locator("#confirm").FillAsync("Beda-12345");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan password" }).ClickAsync();

        await Expect(page.Locator(".gms-banner.error")).ToContainTextAsync("Konfirmasi password tidak sama");
    }

    [Fact]
    public async Task Halaman_terlindungi_mengalihkan_ke_login_dan_kembali_ke_tujuan_setelah_masuk()
    {
        var (user, password) = await stack.Seed.CreateReadyUserAsync();
        var page = await stack.NewPageAsync();

        await page.GotoAsync("/master/uoms");
        await Expect(page).ToHaveURLAsync(new Regex("/login\\?.*returnUrl"));
        await page.GetByLabel("Username").FillAsync(user);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Masuk" }).ClickAsync();

        await Expect(page).ToHaveURLAsync(new Regex("/master/uoms$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Satuan");
    }

    [Fact]
    public async Task Open_redirect_lewat_returnUrl_eksternal_diabaikan()
    {
        var (user, password) = await stack.Seed.CreateReadyUserAsync();
        var page = await stack.NewPageAsync();

        await page.GotoAsync("/login?returnUrl=https%3A%2F%2Fevil.example%2F");
        await page.GetByLabel("Username").FillAsync(user);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Masuk" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Selamat datang");
        Assert.StartsWith(stack.WebUrl, page.Url);
    }

    [Fact]
    public async Task Pengguna_tanpa_izin_diarahkan_ke_halaman_akses_ditolak()
    {
        // Peran "Staf" tanpa izin Administrasi dibuat lewat API.
        var role = await stack.Seed.PostAsync<Gms.Shared.Access.RoleDetail>("/api/v1/roles",
            new Gms.Shared.Access.SaveRoleRequest("Hanya Satuan " + Guid.NewGuid().ToString("N")[..5], null, ["MasterData.UoM.View"]));
        var roles = await stack.Seed.GetAsync<List<Gms.Shared.Access.RoleListItem>>("/api/v1/roles");
        var name = "limit" + Guid.NewGuid().ToString("N")[..5];
        await stack.Seed.PostAsync<Gms.Shared.Access.UserDetail>("/api/v1/users", new Gms.Shared.Access.CreateUserRequest(name, "Pengguna Terbatas", role.Id, "Awal-12345"));
        _ = roles;
        var page = await stack.NewPageAsync();
        await StackFixture.LoginAsync(page, name, "Awal-12345");
        await page.Locator("#current").FillAsync("Awal-12345");
        await page.Locator("#new").FillAsync("Baru-98765");
        await page.Locator("#confirm").FillAsync("Baru-98765");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan password" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Selamat datang");

        await page.GotoAsync("/admin/users");

        await Expect(page).ToHaveURLAsync(new Regex("/forbidden"));
        await page.GotoAsync("/master/uoms");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Satuan");
        await Expect(page.GetByRole(AriaRole.Navigation).GetByText("Pengguna")).ToHaveCountAsync(0); // menu admin tidak terlihat
    }

    [Fact]
    public async Task Logout_mengakhiri_sesi_dan_halaman_terlindungi_kembali_meminta_login()
    {
        var page = await stack.LoginAsAdminAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Selamat datang");

        await page.Locator("button[aria-haspopup=true]").ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Keluar" }).ClickAsync();

        await page.GotoAsync("/stock");
        await Expect(page).ToHaveURLAsync(new Regex("/login"));
    }
}
