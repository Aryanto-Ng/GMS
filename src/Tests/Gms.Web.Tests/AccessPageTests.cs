using AngleSharp.Dom;
using Bunit;
using Gms.Shared.Access;
using Gms.Shared.Security;
using Gms.Web.Components.Pages.Admin;
using Gms.Web.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Web.Tests;

/// <summary>Halaman Administrasi (Pengguna, Role, Audit) terhadap data tiruan yang berperilaku seperti API.</summary>
public sealed class AccessPageTests : WebTestBase
{
    private static readonly string[] AllAdmin = [.. PermissionCatalog.All.Where(p => p.Key.StartsWith("AccessManagement.", StringComparison.Ordinal)).Select(p => p.Key)];

    private readonly MockAccessStore _store = new();

    public AccessPageTests()
    {
        Services.AddSingleton(_store);
        Services.AddSingleton<IUsersApi>(new MockUsersApi(_store));
        Services.AddSingleton<IRolesApi>(new MockRolesApi(_store));
        Services.AddSingleton<IAuditApi>(new MockAuditApi(_store));
        SignIn(AllAdmin);
    }

    private static IElement DialogConfirm(IRenderedComponent<IComponent> cut) => cut.FindAll("dialog button.ant-btn").Last();

    private static void WaitRows<T>(IRenderedComponent<T> cut, int count) where T : IComponent =>
        cut.WaitForAssertion(() => Assert.Equal(count, cut.FindAll("tbody tr").Count));

    // ===== Daftar Pengguna =====

    [Fact]
    public void Daftar_pengguna_menampilkan_baris_dengan_status_ikon_teks_dan_tombol_tambah_sesuai_izin()
    {
        var cut = Render<UsersPage>();

        WaitRows(cut, 4);
        Assert.Contains("Tambah Pengguna", cut.Markup);
        var siti = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("siti"));
        Assert.Contains("Terkunci", siti.TextContent);
        Assert.NotEmpty(siti.QuerySelectorAll(".gms-tag svg, svg"));
        Assert.Contains("4 pengguna", cut.Find(".gms-pageheader-sub").TextContent);
    }

    [Fact]
    public void Tanpa_izin_Create_Unlock_dan_Deactivate_tombolnya_tidak_dirender()
    {
        SignIn(Permissions.AccessManagement.User.View);

        var cut = Render<UsersPage>();

        WaitRows(cut, 4);
        Assert.DoesNotContain("Tambah Pengguna", cut.Markup);
        Assert.Empty(cut.FindAll("tbody button.ant-btn")); // Buka kunci/Nonaktifkan/Aktifkan
        Assert.DoesNotContain(">Ubah<", cut.Markup);
    }

    [Fact]
    public void Filter_status_dan_pencarian_mempersempit_daftar_dan_kosong_menampilkan_state_kosong()
    {
        var cut = Render<UsersPage>();
        WaitRows(cut, 4);

        cut.Find("#user-status").Change("Locked");
        WaitRows(cut, 1);
        Assert.Contains("siti", cut.Markup);

        cut.Find("#user-status").Change("");
        cut.Find("#user-q").Change("tidak-ada-orang-ini");
        cut.WaitForAssertion(() => Assert.Contains("Tidak ada pengguna yang cocok", cut.Markup));
    }

    [Fact]
    public void Buka_kunci_meminta_konfirmasi_lalu_menyegarkan_daftar()
    {
        var cut = Render<UsersPage>();
        WaitRows(cut, 4);

        cut.FindAll("tbody button.ant-btn").Single(b => b.TextContent.Contains("Buka kunci")).Click();
        Assert.Contains("Buka kunci siti?", cut.Find("dialog h2").TextContent);
        Assert.Empty(cut.FindAll("dialog textarea")); // konfirmasi biasa tanpa alasan
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Terkunci", cut.Find("tbody").TextContent));
    }

    [Fact]
    public void Nonaktifkan_dan_Aktifkan_dari_daftar_mengubah_status()
    {
        var cut = Render<UsersPage>();
        WaitRows(cut, 4);

        cut.FindAll("tbody button.ant-btn").Single(b => b.GetAttribute("aria-label") == "Aktifkan andi").Click();
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("Nonaktif", cut.Find("tbody").TextContent.Replace("Nonaktifkan", "")));
    }

    [Fact]
    public void Galat_saat_aksi_ditampilkan_di_dialog_dan_dialog_tetap_terbuka()
    {
        var cut = Render<UsersPage>();
        WaitRows(cut, 4);

        cut.FindAll("tbody button.ant-btn").Single(b => b.GetAttribute("aria-label") == "Nonaktifkan admin").Click();
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Contains("tidak dapat menonaktifkan diri sendiri", cut.Find("dialog").TextContent));
        Assert.Contains("Nonaktifkan admin?", cut.Find("dialog h2").TextContent);
    }

    // ===== Form Pengguna =====

    private IRenderedComponent<UserFormPage> Form(Guid? id = null) => Render<UserFormPage>(p => p.Add(x => x.Id, id));

    [Fact]
    public void Tambah_pengguna_menampilkan_aturan_password_yang_berubah_dan_tombol_Buat_otomatis()
    {
        var cut = Form();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#u-pass")));

        Assert.Equal(3, cut.FindAll(".gms-rules .gms-bad").Count);
        cut.Find("#u-pass").Input("abc12345");
        Assert.Equal(3, cut.FindAll(".gms-rules .gms-ok").Count);

        cut.Find("#u-pass").Input("");
        cut.FindAll("button.ant-btn").Single(b => b.TextContent.Contains("Buat otomatis")).Click();
        Assert.StartsWith("Gms-", cut.Find("#u-pass").GetAttribute("value"));
        Assert.Contains("wajib mengganti password", cut.Markup);
    }

    [Fact]
    public void Simpan_kosong_menampilkan_ringkasan_galat_server_dengan_label_field()
    {
        var cut = Form();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".gms-formsummary")));
        Assert.Contains("Username", cut.Find(".gms-formsummary").TextContent);
    }

    [Fact]
    public void Tambah_pengguna_berhasil_menavigasi_ke_halaman_detail()
    {
        var cut = Form();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#u-name")));
        cut.Find("#u-name").Input("rina");
        cut.Find("#u-full").Input("Rina Wati");
        cut.Find("#u-role").Change(MockAccessStore.StafRoleId.ToString());
        cut.Find("#u-pass").Input("Abc12345");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Matches("/admin/users/[0-9a-f-]{36}$", Nav.Uri));
    }

    [Fact]
    public void Username_duplikat_ditampilkan_sebagai_banner_tanpa_kehilangan_isian()
    {
        var cut = Form();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#u-name")));
        cut.Find("#u-name").Input("staf");
        cut.Find("#u-full").Input("Dobel");
        cut.Find("#u-role").Change(MockAccessStore.StafRoleId.ToString());
        cut.Find("#u-pass").Input("Abc12345");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Username sudah dipakai", cut.Markup));
        Assert.Equal("Dobel", cut.Find("#u-full").GetAttribute("value"));
    }

    [Fact]
    public void Ubah_pengguna_mengunci_username_dan_menonaktifkan_tombol_untuk_administrator_terakhir_dengan_penjelasan()
    {
        var cut = Form(MockAccessStore.AdminUserId);

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#u-name")));
        Assert.True(cut.Find("#u-name").HasAttribute("disabled"));
        var deactivate = cut.FindAll("aside button.ant-btn").Single(b => b.TextContent.Contains("Nonaktifkan"));
        Assert.True(deactivate.HasAttribute("disabled"));
        Assert.Equal("deact-why", deactivate.GetAttribute("aria-describedby"));
        Assert.Contains("Administrator aktif terakhir", cut.Find("#deact-why").TextContent);
    }

    [Fact]
    public void Ubah_nama_menyimpan_dan_memperbarui_rowVersion_sehingga_simpan_kedua_tidak_konflik()
    {
        var cut = Form(MockAccessStore.AdminUserId);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#u-full")));

        cut.Find("#u-full").Input("Admin Satu");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Equal(2u, _store.GetUser(MockAccessStore.AdminUserId).RowVersion));
        cut.Find("#u-full").Input("Admin Dua");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Admin Dua", _store.GetUser(MockAccessStore.AdminUserId).FullName));
        Assert.Empty(cut.FindAll(".gms-banner.error"));
    }

    [Fact]
    public void Reset_password_acak_menampilkan_password_sementara_sekali_dan_dapat_ditutup()
    {
        var cut = Form(MockAccessStore.AdminUserId);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("aside")));

        cut.FindAll("aside button.ant-btn").Single(b => b.TextContent.Contains("Reset Password")).Click();
        Assert.Contains("Reset password admin", cut.Find("dialog h2").TextContent);
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#temp-password")));
        Assert.Contains("tidak ditampilkan lagi", cut.Markup);
        cut.FindAll(".gms-banner button.ant-btn").Single(b => b.TextContent.Contains("Tutup")).Click();
        Assert.Empty(cut.FindAll("#temp-password"));
    }

    [Fact]
    public void Reset_password_manual_pendek_menahan_tombol_Reset()
    {
        var cut = Form(MockAccessStore.AdminUserId);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("aside")));
        cut.FindAll("aside button.ant-btn").Single(b => b.TextContent.Contains("Reset Password")).Click();

        cut.Find("#reset-pass").Input("abc");

        Assert.True(DialogConfirm(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Pengguna_tidak_ditemukan_menampilkan_state_galat_tanpa_Coba_lagi_berlebih()
    {
        var cut = Form(Guid.NewGuid());

        cut.WaitForAssertion(() => { Assert.Contains("Data tidak ditemukan", cut.Markup); Assert.Empty(cut.FindAll(".gms-state button.ant-btn")); });
    }

    // ===== Role =====

    [Fact]
    public void Daftar_role_menandai_role_sistem_dan_menahan_hapus_untuk_role_terpakai()
    {
        var cut = Render<RolesPage>();

        WaitRows(cut, 2);
        var system = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Administrator"));
        Assert.Contains("role sistem", system.TextContent);
        Assert.DoesNotContain("Hapus", system.TextContent);
        var staf = cut.FindAll("tbody tr").Single(r => r.TextContent.Contains("Staf Comfi"));
        var delete = staf.QuerySelector("button.ant-btn")!;
        Assert.True(delete.HasAttribute("disabled"));
        Assert.Contains("Dipakai oleh 3 pengguna", delete.ParentElement!.GetAttribute("title"));
    }

    [Fact]
    public void Hapus_role_kosong_meminta_konfirmasi_lalu_menghilangkannya()
    {
        _store.CreateRole(new SaveRoleRequest("Sementara", null, []));
        var cut = Render<RolesPage>();
        WaitRows(cut, 3);

        cut.FindAll("tbody button.ant-btn").Single(b => b.GetAttribute("aria-label") == "Hapus Sementara").Click();
        Assert.Contains("Hapus role Sementara?", cut.Find("dialog h2").TextContent);
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
    }

    private IRenderedComponent<RoleEditorPage> Editor(Guid? id = null) => Render<RoleEditorPage>(p => p.Add(x => x.Id, id));

    private static IElement Box(IRenderedComponent<RoleEditorPage> cut, string name) => cut.Find($"input[aria-label='{name}']");

    [Fact]
    public void Matriks_dibangun_dari_katalog_server_dengan_grup_modul_dan_grup_Administrasi_terkunci()
    {
        var cut = Editor();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-perm")));
        var modules = cut.FindAll(".gms-groupheader th").Select(t => t.TextContent).ToArray();
        Assert.Contains(modules, m => m.Contains("Administrasi") && m.Contains("Hanya Administrator"));
        Assert.Contains(modules, m => m.Contains("Penjualan"));
        Assert.True(Box(cut, "AccessManagement.User, Lihat".Replace("AccessManagement.User", PermissionCatalog.All.First(p => p.Key == Permissions.AccessManagement.User.View).Resource)).HasAttribute("disabled"));
        Assert.Equal("Matriks hak akses per resource dan aksi", cut.Find("caption").TextContent);
    }

    [Fact]
    public void Mencentang_aksi_otomatis_mencentang_View_dan_melepas_View_melepas_aksi_lain_dengan_toast()
    {
        var cut = Editor();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-perm")));
        var po = PermissionCatalog.All.First(p => p.Key == Permissions.CustomerPO.Create).Resource;

        Box(cut, $"{po}, Buat").Change(true);
        Assert.True(Box(cut, $"{po}, Lihat").HasAttribute("checked"));
        Assert.Contains("Terpilih: 2 permission", cut.Markup);

        Box(cut, $"{po}, Lihat").Change(false);
        Assert.False(Box(cut, $"{po}, Buat").HasAttribute("checked"));
        Assert.Contains("Terpilih: 0 permission", cut.Markup);
    }

    [Fact]
    public void Simpan_role_baru_tanpa_nama_menampilkan_galat_dan_dengan_nama_membuka_ringkasan_lalu_menyimpan()
    {
        var cut = Editor();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-perm")));

        cut.Find("form").Submit();
        Assert.Contains("Nama role wajib diisi", cut.Find(".gms-formsummary").TextContent);

        cut.Find("#r-name").Input("Gudang");
        var po = PermissionCatalog.All.First(p => p.Key == Permissions.CustomerPO.View).Resource;
        Box(cut, $"{po}, Lihat").Change(true);
        cut.Find("form").Submit();
        Assert.Contains("Role baru dengan 1 permission", cut.Find("dialog").TextContent);
        DialogConfirm(cut).Click();

        cut.WaitForAssertion(() => Assert.Matches("/admin/roles/[0-9a-f-]{36}$", Nav.Uri));
        Assert.Contains(_store.ListRoles(), r => r.Name == "Gudang" && r.PermissionCount == 1);
    }

    [Fact]
    public void Role_sistem_hanya_baca_dengan_banner_dan_tanpa_tombol_simpan()
    {
        var cut = Editor(MockAccessStore.AdminRoleId);

        cut.WaitForAssertion(() => Assert.Contains("Role sistem tidak dapat diubah", cut.Markup));
        Assert.All(cut.FindAll("table.gms-perm input[type=checkbox]"), c => Assert.True(c.HasAttribute("disabled")));
        Assert.DoesNotContain(cut.FindAll("form button.ant-btn"), b => b.TextContent.Contains("Simpan"));
        Assert.True(cut.Find("#r-name").HasAttribute("disabled"));
    }

    [Fact]
    public void Ubah_role_biasa_ringkasan_menyebut_tambah_lepas_dan_jumlah_pengguna()
    {
        var cut = Editor(MockAccessStore.StafRoleId);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table.gms-perm")));
        var stok = PermissionCatalog.All.First(p => p.Key == Permissions.StokFG.View).Resource;
        var po = PermissionCatalog.All.First(p => p.Key == Permissions.CustomerPO.View).Resource;

        Box(cut, $"{stok}, Lihat").Change(false);
        Box(cut, $"{po}, Hanya".Replace("Hanya", "Konfirmasi")).Change(true); // menambah Confirm (+View sudah ada)
        cut.Find("form").Submit();

        var text = cut.Find("dialog").TextContent;
        Assert.Contains("Tambah 1, lepas 1 permission.", text);
        Assert.Contains("Berlaku untuk 3 pengguna", text);
    }

    // ===== Audit =====

    [Fact]
    public void Audit_menampilkan_kejadian_rentang_7_hari_dengan_label_dan_detail_dapat_dibuka()
    {
        _store.UpdateRole(MockAccessStore.StafRoleId, new SaveRoleRequest("Staf Comfi", "x", [Permissions.CustomerPO.View], 1));
        var cut = Render<AuditPage>();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));
        Assert.Contains("Login berhasil", cut.Markup);
        Assert.Contains("Role diubah", cut.Markup);
        var detail = cut.FindAll("tbody button.ant-btn").First(b => b.GetAttribute("aria-label")!.Contains("Role diubah"));
        Assert.Equal("false", detail.GetAttribute("aria-expanded"));
        detail.Click();
        Assert.Contains("Sebelum", cut.Find(".gms-auditdetail").TextContent);
        Assert.Equal("true", cut.FindAll("tbody button.ant-btn").First(b => b.GetAttribute("aria-label")!.Contains("Role diubah")).GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Audit_rentang_terlalu_lebar_atau_terbalik_ditolak_dengan_pesan_tanpa_memanggil_API()
    {
        var cut = Render<AuditPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.Find("#a-from").Change("2020-01-01");
        cut.Find("form").Submit();
        Assert.Contains("Rentang terlalu lebar", cut.Find("[role=alert]").TextContent);

        cut.Find("#a-from").Change("2026-12-31");
        cut.Find("#a-to").Change("2026-01-01");
        cut.Find("form").Submit();
        Assert.Contains("Tanggal akhir tidak boleh sebelum tanggal awal", cut.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void Audit_filter_peristiwa_tanpa_hasil_menampilkan_state_kosong()
    {
        var cut = Render<AuditPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("tbody tr")));

        cut.Find("#a-event").Change("RoleDeleted");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Tidak ada kejadian pada rentang ini", cut.Markup));
    }
}
