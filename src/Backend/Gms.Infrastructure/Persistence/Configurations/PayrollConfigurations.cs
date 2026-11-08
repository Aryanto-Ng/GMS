using Gms.Domain.Master;
using Gms.Domain.Payroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

internal sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.ToTable("payroll_periods", t =>
        {
            t.HasCheckConstraint("ck_payroll_periods_dates", "end_date >= start_date");
            t.HasCheckConstraint("ck_payroll_periods_status", "status IN ('Open','Closed')");
        });
        builder.GuidKey();
        builder.UseXmin();
        builder.Property(x => x.Code).AsCode(30);
        builder.Property(x => x.Status).AsEnum();
        builder.Property(x => x.Notes).AsOptionalText(500);
        builder.Property(x => x.TotalPaid).HasPrecision(18, 0);

        builder.HasIndex(x => x.Code).IsUnique();

        // Hanya satu periode Open pada satu waktu (unik pada status dengan filter).
        builder.HasIndex(x => x.Status).IsUnique().HasFilter("status = 'Open'").HasDatabaseName("ux_payroll_periods_single_open");

        // Exclusion constraint "tanpa tumpang tindih" ditambahkan lewat migrasi SQL (AdvancedConstraints).
        builder.HasIndex(x => new { x.StartDate, x.EndDate });
    }
}

internal sealed class PayrollSnapshotLineConfiguration : IEntityTypeConfiguration<PayrollSnapshotLine>
{
    public void Configure(EntityTypeBuilder<PayrollSnapshotLine> builder)
    {
        builder.ToTable("payroll_snapshot_lines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.WorkerCode).AsText(30);
        builder.Property(x => x.WorkerName).AsText(150);
        builder.Property(x => x.WoCode).AsText(30);
        builder.Property(x => x.ProcessCode).AsText(30);
        builder.Property(x => x.ProcessName).AsText(100);
        builder.Property(x => x.SizeCode).AsText(20);
        builder.Property(x => x.Kind).AsEnum();
        builder.Property(x => x.Reason).AsOptionalText(500);
        builder.Property(x => x.Rate).HasPrecision(18, 5);
        builder.Property(x => x.Subtotal).HasPrecision(28, 5);

        builder.HasOne<PayrollPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.PeriodId, x.WorkerId, x.WorkDate });
    }
}

internal sealed class PayrollSnapshotWorkerConfiguration : IEntityTypeConfiguration<PayrollSnapshotWorker>
{
    public void Configure(EntityTypeBuilder<PayrollSnapshotWorker> builder)
    {
        builder.ToTable("payroll_snapshot_workers");
        builder.HasKey(x => new { x.PeriodId, x.WorkerId });
        builder.Property(x => x.WorkerCode).AsText(30);
        builder.Property(x => x.WorkerName).AsText(150);
        builder.Property(x => x.SumBeforeRounding).HasPrecision(28, 5);
        builder.Property(x => x.PaidAmount).HasPrecision(18, 0);
        builder.HasOne<PayrollPeriod>().WithMany().HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Worker>().WithMany().HasForeignKey(x => x.WorkerId).OnDelete(DeleteBehavior.Restrict);
    }
}
