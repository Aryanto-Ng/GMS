using Gms.Domain.Master;
using Gms.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");
        builder.GuidKey();
        builder.UseXmin();
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.CustomerPoNumber).HasColumnType("citext").HasMaxLength(100);
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.Notes).AsOptionalText(1000);
        builder.Property(x => x.CancelReason).AsOptionalText(500);

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.PoDate).IsDescending();

        // Nomor PO Customer unik per customer di antara PO yang tidak dibatalkan (FSD PO AC-PO-03).
        builder.HasIndex(x => new { x.CustomerId, x.CustomerPoNumber })
            .IsUnique()
            .HasFilter("customer_po_number IS NOT NULL AND status <> 'Cancelled'")
            .HasDatabaseName("ux_purchase_orders_customer_po_number");

        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_purchase_orders_status",
            "status IN ('Draft','Confirmed','InProgress','Completed','Cancelled')"));
    }
}

internal sealed class PoLineConfiguration : IEntityTypeConfiguration<PoLine>
{
    public void Configure(EntityTypeBuilder<PoLine> builder)
    {
        builder.ToTable("po_lines", t =>
        {
            t.HasCheckConstraint("ck_po_lines_qty", "qty > 0 AND qty <= 1000000");
            t.HasCheckConstraint("ck_po_lines_counters_non_negative",
                "delivered_qty >= 0 AND allocated_qty >= 0 AND in_production_qty >= 0");

            // Sisa Rencana >= 0 dijaga oleh database (ADR-004).
            t.HasCheckConstraint("ck_po_lines_remaining_plan",
                "delivered_qty + allocated_qty + in_production_qty <= qty");
        });
        builder.GuidKey();
        builder.UseXmin();
        builder.Property(x => x.Notes).AsOptionalText(500);
        builder.Ignore(x => x.RemainingPlanQty);

        builder.HasOne<Fg>().WithMany().HasForeignKey(x => x.FgId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Color>().WithMany().HasForeignKey(x => x.ColorId).OnDelete(DeleteBehavior.Restrict);

        // Tanpa FK ke fg_sizes: PO adalah snapshot (FSD PO edge case E6).
        builder.HasOne<Size>().WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.PoId);
        builder.HasIndex(x => new { x.FgId, x.ColorId, x.SizeId });
        builder.HasIndex(x => x.DeliveryDate).HasFilter("delivered_qty < qty").HasDatabaseName("ix_po_lines_open_delivery_date");
        builder.HasIndex(x => new { x.PoId, x.FgId, x.ColorId, x.SizeId, x.DeliveryDate })
            .IsUnique().HasDatabaseName("ux_po_lines_combination");
    }
}

internal sealed class PoAllocationConfiguration : IEntityTypeConfiguration<PoAllocation>
{
    public void Configure(EntityTypeBuilder<PoAllocation> builder)
    {
        builder.ToTable("po_allocations", t => t.HasCheckConstraint("ck_po_allocations_qty", "qty > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Kind).AsEnum();
        builder.Property(x => x.Source).AsEnum();
        builder.Property(x => x.Reason).AsOptionalText(500);
        builder.HasOne<PoLine>().WithMany().HasForeignKey(x => x.PoLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.PoLineId, x.Id });
    }
}
