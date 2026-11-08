using Gms.Domain.Deliveries;
using Gms.Domain.Inventory;
using Gms.Domain.Master;
using Gms.Domain.Production;
using Gms.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

internal sealed class FgHandoverConfiguration : IEntityTypeConfiguration<FgHandover>
{
    public void Configure(EntityTypeBuilder<FgHandover> builder)
    {
        builder.ToTable("fg_handovers", t =>
            t.HasCheckConstraint("ck_fg_handovers_void", "status <> 'Voided' OR void_reason IS NOT NULL"));
        builder.GuidKey();
        builder.Property(x => x.DocNo).AsOptionalText(100);
        builder.Property(x => x.ReceivedBy).AsOptionalText(150);
        builder.Property(x => x.Notes).AsOptionalText(500);
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.VoidReason).AsOptionalText(500);

        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FgHandover>().WithMany().HasForeignKey(x => x.ReplacesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.WoId);
        builder.HasIndex(x => x.HandoverDate).IsDescending();
    }
}

internal sealed class FgHandoverLineConfiguration : IEntityTypeConfiguration<FgHandoverLine>
{
    public void Configure(EntityTypeBuilder<FgHandoverLine> builder)
    {
        builder.ToTable("fg_handover_lines", t =>
        {
            t.HasCheckConstraint("ck_fg_handover_lines_qty", "qty > 0");
            t.HasCheckConstraint("ck_fg_handover_lines_auto_allocated", "auto_allocated_qty >= 0 AND auto_allocated_qty <= qty");
        });
        builder.HasKey(x => new { x.HandoverId, x.WoLineId });
        builder.HasOne<WoLine>().WithMany().HasForeignKey(x => x.WoLineId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        // Sabuk pengaman terakhir (ADR-004): stok tidak negatif dan teralokasi tidak melebihi stok fisik.
        builder.ToTable("stock_balances", t =>
            t.HasCheckConstraint("ck_stock_balances_valid", "on_hand >= 0 AND allocated >= 0 AND allocated <= on_hand"));
        builder.HasKey(x => new { x.FgId, x.ColorId, x.SizeId });
        builder.Ignore(x => x.Available);
        builder.HasOne<Fg>().WithMany().HasForeignKey(x => x.FgId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Color>().WithMany().HasForeignKey(x => x.ColorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Size>().WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        builder.ToTable("stock_ledger", t => t.HasCheckConstraint("ck_stock_ledger_qty", "qty <> 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Kind).AsEnum(30);
        builder.Property(x => x.SourceType).AsOptionalText(40);
        builder.Property(x => x.Reason).AsOptionalText(500);
        builder.HasOne<Fg>().WithMany().HasForeignKey(x => x.FgId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Color>().WithMany().HasForeignKey(x => x.ColorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Size>().WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);

        // Kartu stok: urut berdasarkan id per FG × Color × Size.
        builder.HasIndex(x => new { x.FgId, x.ColorId, x.SizeId, x.Id });
    }
}

internal sealed class MaterialHandoverConfiguration : IEntityTypeConfiguration<MaterialHandover>
{
    public void Configure(EntityTypeBuilder<MaterialHandover> builder)
    {
        builder.ToTable("material_handovers", t =>
            t.HasCheckConstraint("ck_material_handovers_void", "status <> 'Voided' OR void_reason IS NOT NULL"));
        builder.GuidKey();
        builder.Property(x => x.DocNo).AsOptionalText(100);
        builder.Property(x => x.Notes).AsOptionalText(500);
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.VoidReason).AsOptionalText(500);
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.HandoverId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.WoId);
    }
}

internal sealed class MaterialHandoverLineConfiguration : IEntityTypeConfiguration<MaterialHandoverLine>
{
    public void Configure(EntityTypeBuilder<MaterialHandoverLine> builder)
    {
        builder.ToTable("material_handover_lines", t => t.HasCheckConstraint("ck_material_handover_lines_qty", "qty > 0"));
        builder.GuidKey();
        builder.Property(x => x.Qty).HasPrecision(18, 3);
        builder.Property(x => x.UomCode).AsText(20);
        builder.Property(x => x.Notes).AsOptionalText(500);
        builder.HasOne<Material>().WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries", t =>
        {
            t.HasCheckConstraint("ck_deliveries_void", "status <> 'Voided' OR void_reason IS NOT NULL");
            t.HasCheckConstraint("ck_deliveries_print_count", "print_count >= 0");
        });
        builder.GuidKey();
        builder.UseXmin();
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.RecipientName).AsOptionalText(150);
        builder.Property(x => x.Vehicle).AsOptionalText(50);
        builder.Property(x => x.Driver).AsOptionalText(100);
        builder.Property(x => x.Notes).AsOptionalText(500);
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.VoidReason).AsOptionalText(500);

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.PoId);
        builder.HasIndex(x => x.DeliveryDate).IsDescending();

        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Delivery>().WithMany().HasForeignKey(x => x.ReplacesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeliveryLineConfiguration : IEntityTypeConfiguration<DeliveryLine>
{
    public void Configure(EntityTypeBuilder<DeliveryLine> builder)
    {
        builder.ToTable("delivery_lines", t => t.HasCheckConstraint("ck_delivery_lines_qty", "qty > 0"));
        builder.HasKey(x => new { x.DeliveryId, x.PoLineId });
        builder.HasOne<PoLine>().WithMany().HasForeignKey(x => x.PoLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
