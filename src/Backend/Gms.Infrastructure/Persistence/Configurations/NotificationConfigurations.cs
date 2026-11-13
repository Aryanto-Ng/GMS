using Gms.Domain.Notifications;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gms.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.GuidKey();
        builder.Property(x => x.TypeCode).AsText(10);
        builder.Property(x => x.Category).AsEnum<NotificationCategory>(20);
        builder.Property(x => x.Params).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TargetKind).AsOptionalEnum<NotificationTargetKind>(30);
        builder.Property(x => x.TargetId).AsOptionalText(120);
        builder.Property(x => x.SourceKey).AsText(200);

        builder.HasOne<Gms.Infrastructure.Identity.AppUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);

        // Idempotensi: satu kunci sumber per penerima (pembuatan ulang oleh pekerja/percobaan ulang tidak menggandakan).
        builder.HasIndex(x => new { x.RecipientUserId, x.SourceKey }).IsUnique();
        // Daftar dan filter kategori: terbaru dahulu per penerima.
        builder.HasIndex(x => new { x.RecipientUserId, x.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.RecipientUserId, x.Category, x.CreatedAt }).IsDescending(false, false, true);
        // Lencana: hitung belum dibaca (indeks parsial kecil).
        builder.HasIndex(x => x.RecipientUserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_unread");
        // Retensi.
        builder.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class NotificationEpisodeConfiguration : IEntityTypeConfiguration<NotificationEpisode>
{
    public void Configure(EntityTypeBuilder<NotificationEpisode> builder)
    {
        builder.ToTable("notification_episodes");
        builder.GuidKey();
        builder.Property(x => x.TypeCode).AsText(10);
        builder.Property(x => x.ObjectKey).AsText(100);

        // Satu episode aktif per (jenis, objek): pekerja tidak membuat ganda meski berjalan di dua instance.
        builder.HasIndex(x => new { x.TypeCode, x.ObjectKey }).IsUnique().HasFilter("ended_at IS NULL").HasDatabaseName("ux_notification_episodes_active");
        builder.HasIndex(x => x.EndedAt);
    }
}

internal sealed class NotificationTypeSettingConfiguration : IEntityTypeConfiguration<NotificationTypeSetting>
{
    public void Configure(EntityTypeBuilder<NotificationTypeSetting> builder)
    {
        builder.ToTable("notification_type_settings");
        builder.HasKey(x => x.TypeCode);
        builder.Property(x => x.TypeCode).AsText(10);
        builder.UseXmin();
    }
}

internal sealed class NotificationOptionsConfiguration : IEntityTypeConfiguration<NotificationOptions>
{
    public void Configure(EntityTypeBuilder<NotificationOptions> builder)
    {
        builder.ToTable("notification_options", t =>
        {
            t.HasCheckConstraint("ck_notification_options_single", "id = 1");
            t.HasCheckConstraint("ck_notification_options_due_soon", $"due_soon_days BETWEEN {NotificationLimits.DueSoonDaysMin} AND {NotificationLimits.DueSoonDaysMax}");
            t.HasCheckConstraint("ck_notification_options_retention", $"retention_days BETWEEN {NotificationLimits.RetentionDaysMin} AND {NotificationLimits.RetentionDaysMax}");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ScheduleTime).HasColumnType("time without time zone");
        builder.UseXmin();
    }
}

internal sealed class NotificationJobRunConfiguration : IEntityTypeConfiguration<NotificationJobRun>
{
    public void Configure(EntityTypeBuilder<NotificationJobRun> builder)
    {
        builder.ToTable("notification_job_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Trigger).AsEnum<NotificationJobTrigger>(20);
        builder.Property(x => x.Result).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Error).AsOptionalText(1000);
        builder.HasIndex(x => x.StartedAt).IsDescending();
    }
}
