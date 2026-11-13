namespace Gms.Shared.Security;

/// <summary>Definisi satu permission untuk seeding dan tampilan matriks Role.</summary>
public sealed record PermissionDefinition(
    string Key,
    string Resource,
    string Action,
    string Module,
    string Description,
    int SortOrder,
    bool IsAssignable);

/// <summary>
/// Katalog permission final (docs/Architecture/ApiConventions.md bagian 6). Seeder menambahkan permission yang belum ada
/// tanpa menimpa yang sudah ada. <c>IsAssignable = false</c> berarti hanya Administrator (tidak dapat diberikan ke Role lain).
/// </summary>
public static class PermissionCatalog
{
    private const string ModAdmin = "Administrasi";
    private const string ModMaster = "Master Data";
    private const string ModSales = "Penjualan";
    private const string ModProduction = "Produksi";
    private const string ModWarehouse = "Gudang FG";
    private const string ModPayroll = "Penggajian";
    private const string ModNotif = "Notifikasi";

    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["View"] = "Melihat",
        ["Create"] = "Membuat",
        ["Edit"] = "Mengubah",
        ["Deactivate"] = "Menonaktifkan",
        ["Import"] = "Mengimpor",
        ["Delete"] = "Menghapus",
        ["ResetPassword"] = "Mereset password",
        ["Unlock"] = "Membuka kunci akun",
        ["EndSession"] = "Mengakhiri sesi",
        ["Confirm"] = "Mengonfirmasi",
        ["Cancel"] = "Membatalkan",
        ["Allocate"] = "Mengalokasikan stok",
        ["Release"] = "Merilis",
        ["Complete"] = "Menandai selesai",
        ["Close"] = "Menutup",
        ["Reopen"] = "Membuka kembali",
        ["Void"] = "Membatalkan entri (void)",
        ["Correct"] = "Mengoreksi",
        ["Adjust"] = "Menyesuaikan",
        ["Print"] = "Mencetak",
        ["CreatePeriod"] = "Membuat/mengubah periode",
        ["ClosePeriod"] = "Menutup periode",
        ["Export"] = "Mengekspor",
        ["PrintSlip"] = "Mencetak slip gaji",
    };

    private static readonly string[] Crud = ["View", "Create", "Edit", "Deactivate"];

    /// <summary>Seluruh permission, terurut stabil menurut modul lalu urutan definisi.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } = Build();

    private static List<PermissionDefinition> Build()
    {
        var list = new List<PermissionDefinition>();
        var order = 0;

        void Add(string module, string resource, string label, bool assignable, params string[] actions)
        {
            foreach (var action in actions)
            {
                var text = ActionLabels.TryGetValue(action, out var a) ? a : action;
                list.Add(new PermissionDefinition(
                    $"{resource}.{action}", resource, action, module, $"{text} {label}", ++order, assignable));
            }
        }

        // Administrasi (hanya Administrator)
        Add(ModAdmin, "AccessManagement.User", "pengguna", false, "View", "Create", "Edit", "Deactivate", "ResetPassword", "Unlock", "EndSession");
        Add(ModAdmin, "AccessManagement.Role", "role dan hak akses", false, "View", "Create", "Edit", "Delete");
        Add(ModAdmin, "AccessManagement.AuditLog", "audit log", false, "View");

        // Master Data
        Add(ModMaster, "MasterData.UoM", "satuan", true, Crud);
        Add(ModMaster, "MasterData.KategoriMaterial", "kategori material", true, Crud);
        Add(ModMaster, "MasterData.Material", "material", true, "View", "Create", "Edit", "Deactivate", "Import");
        Add(ModMaster, "MasterData.Size", "size", true, Crud);
        Add(ModMaster, "MasterData.Color", "color", true, Crud);
        Add(ModMaster, "MasterData.Customer", "customer", true, Crud);
        Add(ModMaster, "MasterData.Process", "proses", true, Crud);
        Add(ModMaster, "MasterData.Vendor", "vendor", true, Crud);
        Add(ModMaster, "MasterData.FG", "FG", true, "View", "Create", "Edit", "Deactivate", "Import");
        Add(ModMaster, "MasterData.Routing", "routing FG", true, "View", "Edit");
        Add(ModMaster, "MasterData.Tarif", "tarif", true, "View", "Edit");
        Add(ModMaster, "MasterData.Pekerja", "pekerja", true, "View", "Create", "Edit", "Deactivate", "Import");

        // Penjualan
        Add(ModSales, "CustomerPO", "Customer PO", true, "View", "Create", "Edit", "Confirm", "Cancel");
        Add(ModSales, "CustomerPO.Alokasi", "alokasi stok PO", true, "View", "Allocate", "Release");
        Add(ModSales, "WorkOrder", "Work Order", true, "View", "Create", "Edit", "Release", "Cancel", "Complete", "Close", "Reopen");
        Add(ModSales, "WorkOrder.Tarif", "tarif snapshot Work Order", true, "View");
        Add(ModSales, "Pengiriman", "pengiriman (surat jalan)", true, "View", "Create", "Edit", "Void", "Correct", "Print");

        // Produksi
        Add(ModProduction, "ProductionOutput", "output produksi", true, "View", "Create", "Void", "Correct", "Adjust");
        Add(ModProduction, "VendorJob", "pengiriman/penerimaan vendor", true, "View", "Create", "Void");

        // Gudang FG
        Add(ModWarehouse, "SerahTerimaFG", "serah terima FG", true, "View", "Create", "Void", "Correct");
        Add(ModWarehouse, "SerahTerimaMaterial", "serah terima material", true, "View", "Create", "Void");
        Add(ModWarehouse, "StokFG", "stok FG", true, "View", "Adjust", "Import");

        // Penggajian
        Add(ModPayroll, "Payroll", "penggajian borongan", true, "View", "CreatePeriod", "ClosePeriod", "Export", "PrintSlip");

        // Notifikasi (lonceng dan "Semua notifikasi" tidak butuh izin; hanya pengaturan)
        Add(ModNotif, "Notifikasi.Pengaturan", "pengaturan notifikasi", true, "View", "Edit");

        return list;
    }

    /// <summary>Permission yang harus ikut dipilih bila <paramref name="key"/> dipilih (aturan AC-AM-18): View pada resource yang sama.</summary>
    public static string? RequiredViewFor(string key)
    {
        var lastDot = key.LastIndexOf('.');
        if (lastDot < 0)
        {
            return null;
        }

        var resource = key[..lastDot];
        var action = key[(lastDot + 1)..];
        return action == "View" ? null : $"{resource}.View";
    }
}
