using Gms.Domain.Master;
using Gms.Domain.Production;
using Gms.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders", t =>
        {
            t.HasCheckConstraint("ck_work_orders_dates", "start_date IS NULL OR target_date IS NULL OR target_date >= start_date");
            t.HasCheckConstraint("ck_work_orders_status",
                "status IN ('Draft','Released','InProgress','Completed','Closed','Cancelled')");
        });
        builder.GuidKey();
        builder.UseXmin();
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.FgCode).AsText(50);
        builder.Property(x => x.FgName).AsText(200);
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.Notes).AsOptionalText(1000);
        builder.Property(x => x.CancelReason).AsOptionalText(500);

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.PoId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.FgId);
        builder.HasIndex(x => x.TargetDate)
            .HasFilter("status IN ('Released','InProgress','Completed')")
            .HasDatabaseName("ix_work_orders_open_target_date");

        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Fg>().WithMany().HasForeignKey(x => x.FgId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.WoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(x => x.WoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.RoutingSnapshot).WithOne().HasForeignKey(x => x.WoId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.TariffSnapshot).WithOne().HasForeignKey(x => x.WoId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WoLineConfiguration : IEntityTypeConfiguration<WoLine>
{
    public void Configure(EntityTypeBuilder<WoLine> builder)
    {
        builder.ToTable("wo_lines", t =>
        {
            t.HasCheckConstraint("ck_wo_lines_qty", "qty > 0");
            t.HasCheckConstraint("ck_wo_lines_handed_over", "handed_over_qty >= 0");
        });
        builder.GuidKey();
        builder.UseXmin();
        builder.Property(x => x.ColorCode).AsText(30);
        builder.Property(x => x.ColorName).AsText(100);
        builder.Property(x => x.SizeCode).AsText(20);
        builder.Property(x => x.SizeName).AsText(50);

        builder.HasOne<PoLine>().WithMany().HasForeignKey(x => x.PoLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.WoId, x.PoLineId }).IsUnique();
        builder.HasIndex(x => x.PoLineId);
    }
}

internal sealed class WoStatusHistoryConfiguration : IEntityTypeConfiguration<WoStatusHistory>
{
    public void Configure(EntityTypeBuilder<WoStatusHistory> builder)
    {
        builder.ToTable("wo_status_history");
        builder.GuidKey();
        builder.Property(x => x.FromStatus).AsOptionalEnum();
        builder.Property(x => x.ToStatus).AsEnum();
        builder.Property(x => x.Reason).AsOptionalText(500);
        builder.HasIndex(x => new { x.WoId, x.ChangedAt });
    }
}

internal sealed class WoRoutingSnapshotConfiguration : IEntityTypeConfiguration<WoRoutingSnapshot>
{
    public void Configure(EntityTypeBuilder<WoRoutingSnapshot> builder)
    {
        builder.ToTable("wo_routing_snapshot");
        builder.HasKey(x => new { x.WoId, x.Seq });
        builder.Property(x => x.ProcessCode).AsText(30);
        builder.Property(x => x.ProcessName).AsText(100);
        builder.Property(x => x.ProcessType).AsEnum();
        builder.HasOne<Process>().WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WoTariffSnapshotConfiguration : IEntityTypeConfiguration<WoTariffSnapshot>
{
    public void Configure(EntityTypeBuilder<WoTariffSnapshot> builder)
    {
        builder.ToTable("wo_tariff_snapshot", t => t.HasCheckConstraint("ck_wo_tariff_snapshot_rate", "rate > 0"));
        builder.HasKey(x => new { x.WoId, x.Seq, x.SizeId });
        builder.Property(x => x.SizeCode).AsText(20);
        builder.Property(x => x.Rate).HasPrecision(18, 5);
        builder.HasOne<WoRoutingSnapshot>().WithMany()
            .HasForeignKey(x => new { x.WoId, x.Seq })
            .HasPrincipalKey(x => new { x.WoId, x.Seq })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WoStepBalanceConfiguration : IEntityTypeConfiguration<WoStepBalance>
{
    public void Configure(EntityTypeBuilder<WoStepBalance> builder)
    {
        builder.ToTable("wo_step_balances", t => t.HasCheckConstraint("ck_wo_step_balances_non_negative", "qty >= 0 AND sent_qty >= 0"));
        builder.HasKey(x => new { x.WoLineId, x.Seq });
        builder.HasOne<WoLine>().WithMany().HasForeignKey(x => x.WoLineId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OutputEntryConfiguration : IEntityTypeConfiguration<OutputEntry>
{
    public void Configure(EntityTypeBuilder<OutputEntry> builder)
    {
        builder.ToTable("output_entries", t =>
        {
            t.HasCheckConstraint("ck_output_entries_adjustment",
                "kind <> 'Adjustment' OR (original_date IS NOT NULL AND reason IS NOT NULL)");
            t.HasCheckConstraint("ck_output_entries_void", "status <> 'Voided' OR void_reason IS NOT NULL");
        });
        builder.GuidKey();
        builder.Property(x => x.Kind).AsEnum();
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.Reason).AsOptionalText(500);
        builder.Property(x => x.VoidReason).AsOptionalText(500);

        builder.HasOne<WoRoutingSnapshot>().WithMany()
            .HasForeignKey(x => new { x.WoId, x.Seq })
            .HasPrincipalKey(x => new { x.WoId, x.Seq })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Worker>().WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OutputEntry>().WithMany().HasForeignKey(x => x.ReplacesEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OutputEntry>().WithMany().HasForeignKey(x => x.RefEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.WoId, x.Seq, x.Status });

        // Indeks laporan gaji: entri aktif per tanggal kerja dan pekerja.
        builder.HasIndex(x => new { x.WorkDate, x.WorkerId }).HasFilter("status = 'Active'").HasDatabaseName("ix_output_entries_payroll");
        builder.HasIndex(x => new { x.WorkerId, x.WorkDate });
    }
}

internal sealed class OutputEntryLineConfiguration : IEntityTypeConfiguration<OutputEntryLine>
{
    public void Configure(EntityTypeBuilder<OutputEntryLine> builder)
    {
        builder.ToTable("output_entry_lines", t => t.HasCheckConstraint("ck_output_entry_lines_qty", "qty <> 0"));
        builder.HasKey(x => new { x.EntryId, x.WoLineId });
        builder.HasOne<WoLine>().WithMany().HasForeignKey(x => x.WoLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.WoLineId);
    }
}

internal sealed class VendorMovementConfiguration : IEntityTypeConfiguration<VendorMovement>
{
    public void Configure(EntityTypeBuilder<VendorMovement> builder)
    {
        builder.ToTable("vendor_movements", t =>
            t.HasCheckConstraint("ck_vendor_movements_void", "status <> 'Voided' OR void_reason IS NOT NULL"));
        builder.GuidKey();
        builder.Property(x => x.Kind).AsEnum();
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.DocNo).AsOptionalText(100);
        builder.Property(x => x.Notes).AsOptionalText(500);
        builder.Property(x => x.VoidReason).AsOptionalText(500);

        builder.HasOne<WoRoutingSnapshot>().WithMany()
            .HasForeignKey(x => new { x.WoId, x.Seq })
            .HasPrincipalKey(x => new { x.WoId, x.Seq })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VendorMovement>().WithMany().HasForeignKey(x => x.ReplacesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.MovementId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.WoId, x.Seq, x.VendorId, x.Status });
    }
}

internal sealed class VendorMovementLineConfiguration : IEntityTypeConfiguration<VendorMovementLine>
{
    public void Configure(EntityTypeBuilder<VendorMovementLine> builder)
    {
        builder.ToTable("vendor_movement_lines", t => t.HasCheckConstraint("ck_vendor_movement_lines_qty", "qty > 0"));
        builder.HasKey(x => new { x.MovementId, x.WoLineId });
        builder.HasOne<WoLine>().WithMany().HasForeignKey(x => x.WoLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
