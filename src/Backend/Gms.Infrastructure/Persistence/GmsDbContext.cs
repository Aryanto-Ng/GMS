using Gms.Domain.Access;
using Gms.Domain.Common;
using Gms.Domain.Deliveries;
using Gms.Domain.Inventory;
using Gms.Domain.Master;
using Gms.Domain.Payroll;
using Gms.Domain.Production;
using Gms.Domain.Sales;
using Gms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gms.Infrastructure.Persistence;

/// <summary>
/// DbContext tunggal aplikasi (PostgreSQL). Identity tanpa tabel role bawaan (IdentityUserContext):
/// Role dan Permission adalah entitas domain (lihat docs/Architecture/Database.md).
/// Konfigurasi entitas ditaruh di kelas IEntityTypeConfiguration per modul (folder Configurations/).
/// </summary>
public sealed class GmsDbContext(DbContextOptions<GmsDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    // Access
    public DbSet<AppRole> Roles => Set<AppRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    // Master
    public DbSet<Uom> Uoms => Set<Uom>();
    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Size> Sizes => Set<Size>();
    public DbSet<Color> Colors => Set<Color>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Worker> Workers => Set<Worker>();
    public DbSet<Fg> Fgs => Set<Fg>();
    public DbSet<FgSize> FgSizes => Set<FgSize>();
    public DbSet<FgRoutingStep> FgRoutingSteps => Set<FgRoutingStep>();
    public DbSet<FgTariff> FgTariffs => Set<FgTariff>();

    // Sales
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PoLine> PoLines => Set<PoLine>();
    public DbSet<PoAllocation> PoAllocations => Set<PoAllocation>();

    // Production
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WoLine> WoLines => Set<WoLine>();
    public DbSet<WoStatusHistory> WoStatusHistory => Set<WoStatusHistory>();
    public DbSet<WoRoutingSnapshot> WoRoutingSnapshots => Set<WoRoutingSnapshot>();
    public DbSet<WoTariffSnapshot> WoTariffSnapshots => Set<WoTariffSnapshot>();
    public DbSet<WoStepBalance> WoStepBalances => Set<WoStepBalance>();
    public DbSet<OutputEntry> OutputEntries => Set<OutputEntry>();
    public DbSet<OutputEntryLine> OutputEntryLines => Set<OutputEntryLine>();
    public DbSet<VendorMovement> VendorMovements => Set<VendorMovement>();
    public DbSet<VendorMovementLine> VendorMovementLines => Set<VendorMovementLine>();

    // Inventory
    public DbSet<FgHandover> FgHandovers => Set<FgHandover>();
    public DbSet<FgHandoverLine> FgHandoverLines => Set<FgHandoverLine>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockLedgerEntry> StockLedger => Set<StockLedgerEntry>();
    public DbSet<MaterialHandover> MaterialHandovers => Set<MaterialHandover>();
    public DbSet<MaterialHandoverLine> MaterialHandoverLines => Set<MaterialHandoverLine>();

    // Deliveries
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryLine> DeliveryLines => Set<DeliveryLine>();

    // Payroll
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<PayrollSnapshotLine> PayrollSnapshotLines => Set<PayrollSnapshotLine>();
    public DbSet<PayrollSnapshotWorker> PayrollSnapshotWorkers => Set<PayrollSnapshotWorker>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // citext: kode/username unik tanpa peka huruf besar-kecil. btree_gist: exclusion constraint periode gaji.
        builder.HasPostgresExtension("citext");
        builder.HasPostgresExtension("btree_gist");

        // Tabel bawaan Identity yang dipakai/dibuat.
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(GmsDbContext).Assembly);

        // Harus terakhir: menyesuaikan nama tabel/kolom yang belum eksplisit menjadi snake_case.
        SnakeCaseNaming.Apply(builder);
    }
}
