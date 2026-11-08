using Gms.Domain.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

internal sealed class UomConfiguration : IEntityTypeConfiguration<Uom>
{
    public void Configure(EntityTypeBuilder<Uom> builder)
    {
        builder.ConfigureMaster("uoms");
        builder.Property(x => x.Code).AsCode(20);
        builder.Property(x => x.Name).AsText(100);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class MaterialCategoryConfiguration : IEntityTypeConfiguration<MaterialCategory>
{
    public void Configure(EntityTypeBuilder<MaterialCategory> builder)
    {
        builder.ConfigureMaster("material_categories");
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.Name).AsText(100);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ConfigureMaster("materials");
        builder.Property(x => x.Code).AsCode(50);
        builder.Property(x => x.Name).AsText(200);
        builder.Property(x => x.Description).AsOptionalText(1000);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasOne<MaterialCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Uom>().WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SizeConfiguration : IEntityTypeConfiguration<Size>
{
    public void Configure(EntityTypeBuilder<Size> builder)
    {
        builder.ConfigureMaster("sizes");
        builder.Property(x => x.Code).AsCode(20);
        builder.Property(x => x.Name).AsText(50);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SortOrder);
        builder.ToTable(t => t.HasCheckConstraint("ck_sizes_sort_order", "sort_order >= 0"));
    }
}

internal sealed class ColorConfiguration : IEntityTypeConfiguration<Color>
{
    public void Configure(EntityTypeBuilder<Color> builder)
    {
        builder.ConfigureMaster("colors");
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.Name).AsText(100);
        builder.Property(x => x.Hex).HasMaxLength(7).IsFixedLength();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("ck_colors_hex", "hex IS NULL OR hex ~ '^#[0-9A-Fa-f]{6}$'"));
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ConfigureMaster("customers");
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.Name).HasColumnType("citext").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContactPerson).AsOptionalText(150);
        builder.Property(x => x.Phone).AsOptionalText(50);
        builder.Property(x => x.Address).AsOptionalText(500);
        builder.Property(x => x.Notes).AsOptionalText(1000);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class ProcessConfiguration : IEntityTypeConfiguration<Process>
{
    public void Configure(EntityTypeBuilder<Process> builder)
    {
        builder.ConfigureMaster("processes");
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.Name).AsText(100);
        builder.Property(x => x.Type).AsEnum();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("ck_processes_type", "type IN ('Internal','Vendor')"));
    }
}

internal sealed class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> builder)
    {
        builder.ConfigureMaster("vendors");
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.Name).AsText(200);
        builder.Property(x => x.ServiceType).AsOptionalText(100);
        builder.Property(x => x.ContactPerson).AsOptionalText(150);
        builder.Property(x => x.Phone).AsOptionalText(50);
        builder.Property(x => x.Address).AsOptionalText(500);
        builder.Property(x => x.Notes).AsOptionalText(1000);
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class WorkerConfiguration : IEntityTypeConfiguration<Worker>
{
    public void Configure(EntityTypeBuilder<Worker> builder)
    {
        builder.ConfigureMaster("workers");
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.Name).AsText(150);
        builder.Property(x => x.Notes).AsOptionalText(1000);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.Name);
        builder.HasOne<Process>().WithMany().HasForeignKey(x => x.MainProcessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FgConfiguration : IEntityTypeConfiguration<Fg>
{
    public void Configure(EntityTypeBuilder<Fg> builder)
    {
        builder.ConfigureMaster("fgs");
        builder.Property(x => x.Code).AsCode(50);
        builder.Property(x => x.Name).AsText(200);
        builder.Property(x => x.Kind).AsOptionalText(100);
        builder.Property(x => x.Notes).AsOptionalText(1000);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Sizes).WithOne().HasForeignKey(x => x.FgId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.RoutingSteps).WithOne().HasForeignKey(x => x.FgId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Tariffs).WithOne().HasForeignKey(x => x.FgId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FgSizeConfiguration : IEntityTypeConfiguration<FgSize>
{
    public void Configure(EntityTypeBuilder<FgSize> builder)
    {
        builder.ToTable("fg_sizes");
        builder.HasKey(x => new { x.FgId, x.SizeId });
        builder.HasOne<Size>().WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FgRoutingStepConfiguration : IEntityTypeConfiguration<FgRoutingStep>
{
    public void Configure(EntityTypeBuilder<FgRoutingStep> builder)
    {
        builder.ToTable("fg_routing_steps", t => t.HasCheckConstraint("ck_fg_routing_steps_seq", "seq >= 1"));
        builder.GuidKey();

        // Satu Process hanya sekali per FG. Juga menjadi kunci prinsipal FK komposit dari fg_tariffs.
        builder.HasAlternateKey(x => new { x.FgId, x.ProcessId }).HasName("ux_fg_routing_steps_fg_process");
        builder.HasOne<Process>().WithMany().HasForeignKey(x => x.ProcessId).OnDelete(DeleteBehavior.Restrict);

        // Unik (fg_id, seq) dibuat DEFERRABLE lewat migrasi SQL (lihat migrasi AdvancedConstraints);
        // sengaja tidak dideklarasikan di sini agar tidak menjadi indeks ganda.
    }
}

internal sealed class FgTariffConfiguration : IEntityTypeConfiguration<FgTariff>
{
    public void Configure(EntityTypeBuilder<FgTariff> builder)
    {
        builder.ToTable("fg_tariffs", t => t.HasCheckConstraint("ck_fg_tariffs_rate", "rate > 0"));
        builder.HasKey(x => new { x.FgId, x.ProcessId, x.SizeId });
        builder.Property(x => x.Rate).HasPrecision(18, 5);

        // Tarif hanya sah untuk langkah routing dan size berlaku FG tersebut; hapus langkah/size => tarif ikut terhapus.
        builder.HasOne<FgRoutingStep>().WithMany()
            .HasForeignKey(x => new { x.FgId, x.ProcessId })
            .HasPrincipalKey(x => new { x.FgId, x.ProcessId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FgSize>().WithMany()
            .HasForeignKey(x => new { x.FgId, x.SizeId })
            .HasPrincipalKey(x => new { x.FgId, x.SizeId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
