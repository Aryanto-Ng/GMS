using Gms.Shared.Security;
using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace Gms.Web.Components.Layout;

/// <summary>
/// Satu item navigasi. <see cref="Enabled"/> baru true setelah halaman modulnya ada; item yang belum dibangun tidak ditampilkan
/// (tidak ada tautan mati). Ubah ke true saat modul selesai.
/// </summary>
public sealed record NavItem(string Title, string Href, string Permission, Icon Icon, bool Enabled = false);

public sealed record NavGroup(string Title, IReadOnlyList<NavItem> Items, bool CollapsedByDefault = false);

/// <summary>Navigasi dikelompokkan per fungsi (_Foundation-UI.md 3.1). Visibilitas = permission View masing-masing.</summary>
public static class NavCatalog
{
    private static Icon I<T>() where T : Icon, new() => new T();

    public static readonly IReadOnlyList<NavGroup> Groups =
    [
        new("Penjualan",
        [
            new("Customer PO", "/purchase-orders", Permissions.CustomerPO.View, I<Icons.Regular.Size20.DocumentText>()),
            new("Work Order", "/work-orders", Permissions.WorkOrder.View, I<Icons.Regular.Size20.ClipboardTaskListLtr>()),
            new("Pengiriman", "/deliveries", Permissions.Pengiriman.View, I<Icons.Regular.Size20.VehicleTruckProfile>()),
        ]),
        new("Gudang FG",
        [
            new("Stok FG", "/stock", Permissions.StokFG.View, I<Icons.Regular.Size20.Box>()),
            new("Serah Terima FG", "/handovers/fg", Permissions.SerahTerimaFG.View, I<Icons.Regular.Size20.ArrowSwap>()),
            new("Serah Terima Material", "/handovers/material", Permissions.SerahTerimaMaterial.View, I<Icons.Regular.Size20.Cube>()),
        ]),
        new("Produksi",
        [
            new("Input Output", "/production/output", Permissions.ProductionOutput.Create, I<Icons.Regular.Size20.TableEdit>()),
            new("Kirim/Terima Vendor", "/production/vendor", Permissions.VendorJob.View, I<Icons.Regular.Size20.ArrowUpload>()),
            new("Riwayat Produksi", "/production/history", Permissions.ProductionOutput.View, I<Icons.Regular.Size20.History>()),
        ]),
        new("Penggajian",
        [
            new("Periode Gaji", "/payroll", Permissions.Payroll.View, I<Icons.Regular.Size20.Money>()),
        ]),
        new("Master Data",
        [
            new("Customer", "/master/customers", Permissions.MasterData.Customer.View, I<Icons.Regular.Size20.People>(), Enabled: true),
            new("FG", "/master/fgs", Permissions.MasterData.FG.View, I<Icons.Regular.Size20.ShoppingBag>(), Enabled: true),
            new("Material", "/master/materials", Permissions.MasterData.Material.View, I<Icons.Regular.Size20.Layer>(), Enabled: true),
            new("Kategori Material", "/master/material-categories", Permissions.MasterData.KategoriMaterial.View, I<Icons.Regular.Size20.Tag>(), Enabled: true),
            new("Satuan", "/master/uoms", Permissions.MasterData.UoM.View, I<Icons.Regular.Size20.Ruler>(), Enabled: true),
            new("Size", "/master/sizes", Permissions.MasterData.Size.View, I<Icons.Regular.Size20.TextFontSize>(), Enabled: true),
            new("Color", "/master/colors", Permissions.MasterData.Color.View, I<Icons.Regular.Size20.Color>(), Enabled: true),
            new("Process", "/master/processes", Permissions.MasterData.Process.View, I<Icons.Regular.Size20.Flowchart>(), Enabled: true),
            new("Vendor", "/master/vendors", Permissions.MasterData.Vendor.View, I<Icons.Regular.Size20.Building>(), Enabled: true),
            new("Pekerja", "/master/workers", Permissions.MasterData.Pekerja.View, I<Icons.Regular.Size20.PersonBoard>(), Enabled: true),
        ], CollapsedByDefault: true),
        new("Administrasi",
        [
            new("Pengguna", "/admin/users", Permissions.AccessManagement.User.View, I<Icons.Regular.Size20.Person>(), Enabled: true),
            new("Role & Hak Akses", "/admin/roles", Permissions.AccessManagement.Role.View, I<Icons.Regular.Size20.ShieldKeyhole>(), Enabled: true),
            new("Audit Log", "/admin/audit", Permissions.AccessManagement.AuditLog.View, I<Icons.Regular.Size20.ClipboardBulletListLtr>(), Enabled: true),
        ]),
    ];

    /// <summary>Grup beserta item yang boleh dilihat pengguna dan sudah tersedia; grup tanpa item terlihat disembunyikan.</summary>
    public static IReadOnlyList<NavGroup> VisibleFor(Func<string, bool> hasPermission, IReadOnlyList<NavGroup>? catalog = null) =>
        (catalog ?? Groups)
            .Select(g => g with { Items = g.Items.Where(i => i.Enabled && hasPermission(i.Permission)).ToList() })
            .Where(g => g.Items.Count > 0)
            .ToList();
}
