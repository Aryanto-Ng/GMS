using Gms.Shared.Security;

namespace Gms.Web.Components.Layout;

/// <summary>Jenis entri katalog: halaman (daftar, laporan, detail) atau aksi (Buat, Input, Kirim, Terima).</summary>
public enum NavKind { Page, Action }

/// <summary>
/// Satu entri katalog navigasi (Design.md 7.1). <see cref="Permission"/> = izin yang dibutuhkan (View untuk halaman, Create/aksi untuk aksi);
/// entri tanpa izin tidak dirender di MegaMenu maupun CommandPalette. <see cref="Keywords"/> = sinonim sehari-hari untuk pencarian.
/// </summary>
public sealed record NavEntry(string Key, string Label, string Desc, string Href, string Permission, NavKind Kind = NavKind.Page, string Keywords = "");

/// <summary>Grup di dalam kategori (kolom pada panel MegaMenu).</summary>
public sealed record NavGroup(string Title, IReadOnlyList<NavEntry> Entries);

/// <summary>Kategori tingkat atas (trigger horizontal MegaMenu). Ikon = tipe ikon AntDesign.</summary>
public sealed record NavCategory(string Key, string Title, string Icon, IReadOnlyList<NavGroup> Groups);

/// <summary>Baris datar untuk CommandPalette: entri beserta jalur "Kategori › Grup".</summary>
public sealed record NavFlatEntry(string Key, string Label, string Desc, string Href, NavKind Kind, string Keywords, string Category, string Group, string CategoryKey = "");

/// <summary>
/// Sumber tunggal navigasi (Design.md 7): Kategori → Grup → Halaman/Aksi. Beranda adalah tautan langsung tanpa panel.
/// Menambah halaman = menambah entri di sini; tidak ada jalur navigasi lain.
/// </summary>
public static class NavCatalog
{
    public static readonly NavEntry Home = new("home", "Beranda", "Ringkasan dan aksi cepat", "/", string.Empty, NavKind.Page, "dashboard ringkasan");

    private static NavEntry P(string key, string label, string desc, string href, string permission, string keywords = "") =>
        new(key, label, desc, href, permission, NavKind.Page, keywords);

    private static NavEntry A(string key, string label, string desc, string href, string permission, string keywords = "") =>
        new(key, label, desc, href, permission, NavKind.Action, keywords);

    public static readonly IReadOnlyList<NavCategory> Categories =
    [
        new("sales", "Penjualan", "file-text",
        [
            new("Customer PO",
            [
                P("po.list", "Daftar PO", "Pesanan customer dan statusnya", "/purchase-orders", Permissions.CustomerPO.View, "pesanan order purchase"),
                A("po.new", "Buat PO", "Catat pesanan customer baru", "/purchase-orders/new", Permissions.CustomerPO.Create, "tambah pesanan order"),
            ]),
            new("Pengiriman",
            [
                P("sj.list", "Daftar Surat Jalan", "Surat jalan dan statusnya", "/deliveries", Permissions.Pengiriman.View, "pengiriman kirim sj"),
                A("sj.new", "Buat Surat Jalan", "Kirim barang jadi ke customer", "/deliveries/new", Permissions.Pengiriman.Create, "kirim sj surat jalan baru"),
            ]),
        ]),
        new("production", "Produksi", "tool",
        [
            new("Work Order",
            [
                P("wo.list", "Daftar WO", "Work order dan progres langkah", "/work-orders", Permissions.WorkOrder.View, "wo perintah kerja"),
                A("wo.new", "Buat WO", "Terbitkan work order dari PO", "/work-orders/new", Permissions.WorkOrder.Create, "tambah wo perintah kerja"),
            ]),
            new("Production Output",
            [
                P("out.history", "Riwayat Output", "Riwayat output, penyesuaian, dan void", "/production/history", Permissions.ProductionOutput.View, "riwayat produksi hasil"),
                A("out.new", "Input Output", "Catat hasil kerja per pekerja", "/production/output", Permissions.ProductionOutput.Create, "hasil kerja borongan"),
                A("out.adjust", "Input Penyesuaian", "Koreksi output yang sudah tercatat", "/production/adjustments", Permissions.ProductionOutput.Adjust, "koreksi sesuaikan"),
            ]),
            new("Vendor",
            [
                A("vendor.move", "Kirim/Terima Vendor", "Catat barang keluar dan masuk vendor", "/production/vendor", Permissions.VendorJob.View, "vendor bordir kirim terima"),
            ]),
        ]),
        new("warehouse", "Gudang", "inbox",
        [
            new("Finished Goods",
            [
                P("fg.handovers", "Daftar Serah Terima", "Serah terima barang jadi ke gudang", "/handovers/fg", Permissions.SerahTerimaFG.View, "serah terima fg"),
                P("fg.stock", "Stok FG", "Stok barang jadi saat ini", "/stock", Permissions.StokFG.View, "stok barang jadi gudang"),
                P("fg.ledger", "Kartu Stok", "Mutasi stok per barang", "/stock/ledger", Permissions.StokFG.View, "mutasi riwayat stok"),
                A("fg.handover.new", "Serah Terima FG", "Terima barang jadi dari produksi", "/handovers/fg/new", Permissions.SerahTerimaFG.Create, "terima fg"),
            ]),
            new("Material",
            [
                P("mat.handovers", "Serah Terima Material", "Material keluar ke produksi", "/handovers/material", Permissions.SerahTerimaMaterial.View, "material bahan"),
                A("mat.handover.new", "Serah Terima Material Baru", "Catat material keluar ke produksi", "/handovers/material/new", Permissions.SerahTerimaMaterial.Create, "bahan keluar"),
            ]),
            new("Impor",
            [
                A("stock.import", "Impor Saldo Awal", "Muat saldo stok awal dari CSV", "/stock/import", Permissions.StokFG.Import, "csv saldo awal opening"),
            ]),
        ]),
        new("payroll", "Penggajian", "wallet",
        [
            new("Payroll Borongan",
            [
                P("payroll.periods", "Periode Gaji", "Periode, rincian, slip, dan penutupan", "/payroll", Permissions.Payroll.View, "gaji payroll slip upah"),
            ]),
        ]),
        new("master", "Master Data", "database",
        [
            new("Produk",
            [
                P("fgs", "Finished Goods", "Barang jadi, size, routing, dan tarif", "/master/fgs", Permissions.MasterData.FG.View, "fg produk tarif routing"),
                P("materials", "Material", "Bahan dan satuannya", "/master/materials", Permissions.MasterData.Material.View, "bahan"),
                P("matcats", "Kategori Material", "Pengelompokan material", "/master/material-categories", Permissions.MasterData.KategoriMaterial.View, "kategori bahan"),
            ]),
            new("Atribut",
            [
                P("sizes", "Size", "Daftar ukuran", "/master/sizes", Permissions.MasterData.Size.View, "ukuran"),
                P("colors", "Color", "Daftar warna", "/master/colors", Permissions.MasterData.Color.View, "warna"),
                P("uoms", "Satuan (UoM)", "Satuan ukur", "/master/uoms", Permissions.MasterData.UoM.View, "unit satuan"),
                P("processes", "Process", "Proses produksi", "/master/processes", Permissions.MasterData.Process.View, "proses langkah"),
            ]),
            new("Mitra & SDM",
            [
                P("customers", "Customer", "Pelanggan", "/master/customers", Permissions.MasterData.Customer.View, "pelanggan"),
                P("vendors", "Vendor", "Mitra pengerjaan luar", "/master/vendors", Permissions.MasterData.Vendor.View, "mitra"),
                P("workers", "Pekerja", "Pekerja borongan", "/master/workers", Permissions.MasterData.Pekerja.View, "karyawan sdm"),
            ]),
            new("Impor",
            [
                A("import.fgs", "Impor FG", "Muat barang jadi dari CSV", "/master/fgs/import", Permissions.MasterData.FG.Import, "csv fg"),
                A("import.materials", "Impor Material", "Muat material dari CSV", "/master/materials/import", Permissions.MasterData.Material.Import, "csv bahan"),
                A("import.workers", "Impor Pekerja", "Muat pekerja dari CSV", "/master/workers/import", Permissions.MasterData.Pekerja.Import, "csv karyawan"),
            ]),
        ]),
        new("admin", "Administrasi", "safety",
        [
            new("Hak Akses",
            [
                P("users", "Pengguna", "Akun pengguna", "/admin/users", Permissions.AccessManagement.User.View, "user akun login"),
                P("roles", "Role & Hak Akses", "Role dan matriks izin", "/admin/roles", Permissions.AccessManagement.Role.View, "peran permission izin"),
                P("audit", "Audit Log", "Jejak perubahan data", "/admin/audit", Permissions.AccessManagement.AuditLog.View, "log riwayat audit"),
            ]),
            new("Notifikasi",
            [
                P("notif.settings", "Pengaturan Notifikasi", "Jenis, ambang, jam, masa simpan", "/admin/notifications", Permissions.Notifikasi.Pengaturan.View, "notifikasi lonceng pemberitahuan"),
            ]),
        ]),
    ];

    /// <summary>Katalog yang boleh dilihat pengguna: entri tanpa izin, grup kosong, dan kategori kosong dibuang.</summary>
    public static IReadOnlyList<NavCategory> VisibleFor(Func<string, bool> hasPermission, IReadOnlyList<NavCategory>? catalog = null) =>
        (catalog ?? Categories)
            .Select(c => c with
            {
                Groups = c.Groups
                    .Select(g => g with { Entries = g.Entries.Where(e => hasPermission(e.Permission)).ToList() })
                    .Where(g => g.Entries.Count > 0)
                    .ToList(),
            })
            .Where(c => c.Groups.Count > 0)
            .ToList();

    /// <summary>Daftar datar (Beranda + semua entri terlihat) untuk CommandPalette.</summary>
    public static IReadOnlyList<NavFlatEntry> Flatten(IReadOnlyList<NavCategory> visible) =>
        visible
            .SelectMany(c => c.Groups.SelectMany(g => g.Entries.Select(e => new NavFlatEntry(e.Key, e.Label, e.Desc, e.Href, e.Kind, e.Keywords, c.Title, g.Title, c.Key))))
            .Prepend(new NavFlatEntry(Home.Key, Home.Label, Home.Desc, Home.Href, NavKind.Page, Home.Keywords, string.Empty, string.Empty, string.Empty))
            .ToList();
}
