namespace Gms.Shared.Security;

/// <summary>
/// Kunci permission bertipe kuat, format <c>Resource.Action</c>. Sumber kebenaran daftar ada di
/// <see cref="PermissionCatalog"/> (di-seed ke tabel permissions). Backend memiliki tes yang memastikan
/// setiap konstanta di sini terdaftar pada katalog. Lihat docs/Architecture/ApiConventions.md bagian 6.
/// </summary>
public static class Permissions
{
    public static class AccessManagement
    {
        public static class User
        {
            public const string View = "AccessManagement.User.View";
            public const string Create = "AccessManagement.User.Create";
            public const string Edit = "AccessManagement.User.Edit";
            public const string Deactivate = "AccessManagement.User.Deactivate";
            public const string ResetPassword = "AccessManagement.User.ResetPassword";
            public const string Unlock = "AccessManagement.User.Unlock";
            public const string EndSession = "AccessManagement.User.EndSession";
        }

        public static class Role
        {
            public const string View = "AccessManagement.Role.View";
            public const string Create = "AccessManagement.Role.Create";
            public const string Edit = "AccessManagement.Role.Edit";
            public const string Delete = "AccessManagement.Role.Delete";
        }

        public static class AuditLog
        {
            public const string View = "AccessManagement.AuditLog.View";
        }
    }

    public static class MasterData
    {
        public static class UoM
        {
            public const string View = "MasterData.UoM.View";
            public const string Create = "MasterData.UoM.Create";
            public const string Edit = "MasterData.UoM.Edit";
            public const string Deactivate = "MasterData.UoM.Deactivate";
        }

        public static class KategoriMaterial
        {
            public const string View = "MasterData.KategoriMaterial.View";
            public const string Create = "MasterData.KategoriMaterial.Create";
            public const string Edit = "MasterData.KategoriMaterial.Edit";
            public const string Deactivate = "MasterData.KategoriMaterial.Deactivate";
        }

        public static class Material
        {
            public const string View = "MasterData.Material.View";
            public const string Create = "MasterData.Material.Create";
            public const string Edit = "MasterData.Material.Edit";
            public const string Deactivate = "MasterData.Material.Deactivate";
            public const string Import = "MasterData.Material.Import";
        }

        public static class Size
        {
            public const string View = "MasterData.Size.View";
            public const string Create = "MasterData.Size.Create";
            public const string Edit = "MasterData.Size.Edit";
            public const string Deactivate = "MasterData.Size.Deactivate";
        }

        public static class Color
        {
            public const string View = "MasterData.Color.View";
            public const string Create = "MasterData.Color.Create";
            public const string Edit = "MasterData.Color.Edit";
            public const string Deactivate = "MasterData.Color.Deactivate";
        }

        public static class Customer
        {
            public const string View = "MasterData.Customer.View";
            public const string Create = "MasterData.Customer.Create";
            public const string Edit = "MasterData.Customer.Edit";
            public const string Deactivate = "MasterData.Customer.Deactivate";
        }

        public static class Process
        {
            public const string View = "MasterData.Process.View";
            public const string Create = "MasterData.Process.Create";
            public const string Edit = "MasterData.Process.Edit";
            public const string Deactivate = "MasterData.Process.Deactivate";
        }

        public static class Vendor
        {
            public const string View = "MasterData.Vendor.View";
            public const string Create = "MasterData.Vendor.Create";
            public const string Edit = "MasterData.Vendor.Edit";
            public const string Deactivate = "MasterData.Vendor.Deactivate";
        }

        public static class FG
        {
            public const string View = "MasterData.FG.View";
            public const string Create = "MasterData.FG.Create";
            public const string Edit = "MasterData.FG.Edit";
            public const string Deactivate = "MasterData.FG.Deactivate";
            public const string Import = "MasterData.FG.Import";
        }

        public static class Routing
        {
            public const string View = "MasterData.Routing.View";
            public const string Edit = "MasterData.Routing.Edit";
        }

        public static class Tarif
        {
            public const string View = "MasterData.Tarif.View";
            public const string Edit = "MasterData.Tarif.Edit";
        }

        public static class Pekerja
        {
            public const string View = "MasterData.Pekerja.View";
            public const string Create = "MasterData.Pekerja.Create";
            public const string Edit = "MasterData.Pekerja.Edit";
            public const string Deactivate = "MasterData.Pekerja.Deactivate";
            public const string Import = "MasterData.Pekerja.Import";
        }
    }

    public static class CustomerPO
    {
        public const string View = "CustomerPO.View";
        public const string Create = "CustomerPO.Create";
        public const string Edit = "CustomerPO.Edit";
        public const string Confirm = "CustomerPO.Confirm";
        public const string Cancel = "CustomerPO.Cancel";

        public static class Alokasi
        {
            public const string View = "CustomerPO.Alokasi.View";
            public const string Allocate = "CustomerPO.Alokasi.Allocate";
            public const string Release = "CustomerPO.Alokasi.Release";
        }
    }

    public static class WorkOrder
    {
        public const string View = "WorkOrder.View";
        public const string Create = "WorkOrder.Create";
        public const string Edit = "WorkOrder.Edit";
        public const string Release = "WorkOrder.Release";
        public const string Cancel = "WorkOrder.Cancel";
        public const string Complete = "WorkOrder.Complete";
        public const string Close = "WorkOrder.Close";
        public const string Reopen = "WorkOrder.Reopen";

        public static class Tarif
        {
            public const string View = "WorkOrder.Tarif.View";
        }
    }

    public static class ProductionOutput
    {
        public const string View = "ProductionOutput.View";
        public const string Create = "ProductionOutput.Create";
        public const string Void = "ProductionOutput.Void";
        public const string Correct = "ProductionOutput.Correct";
        public const string Adjust = "ProductionOutput.Adjust";
    }

    public static class VendorJob
    {
        public const string View = "VendorJob.View";
        public const string Create = "VendorJob.Create";
        public const string Void = "VendorJob.Void";
    }

    public static class SerahTerimaFG
    {
        public const string View = "SerahTerimaFG.View";
        public const string Create = "SerahTerimaFG.Create";
        public const string Void = "SerahTerimaFG.Void";
        public const string Correct = "SerahTerimaFG.Correct";
    }

    public static class SerahTerimaMaterial
    {
        public const string View = "SerahTerimaMaterial.View";
        public const string Create = "SerahTerimaMaterial.Create";
        public const string Void = "SerahTerimaMaterial.Void";
    }

    public static class StokFG
    {
        public const string View = "StokFG.View";
        public const string Adjust = "StokFG.Adjust";
        public const string Import = "StokFG.Import";
    }

    public static class Pengiriman
    {
        public const string View = "Pengiriman.View";
        public const string Create = "Pengiriman.Create";
        public const string Edit = "Pengiriman.Edit";
        public const string Void = "Pengiriman.Void";
        public const string Correct = "Pengiriman.Correct";
        public const string Print = "Pengiriman.Print";
    }

    public static class Payroll
    {
        public const string View = "Payroll.View";
        public const string CreatePeriod = "Payroll.CreatePeriod";
        public const string ClosePeriod = "Payroll.ClosePeriod";
        public const string Export = "Payroll.Export";
        public const string PrintSlip = "Payroll.PrintSlip";
    }
}
