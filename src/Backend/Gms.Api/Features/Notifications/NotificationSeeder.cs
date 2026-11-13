using System.Globalization;
using Gms.Api.Hosting;
using Gms.Domain.Notifications;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Features.Notifications;

/// <summary>Konfigurasi bawaan (bagian <c>Notifications</c>). Hanya nilai AWAL seeder; selanjutnya dikelola dari tabel pengaturan.</summary>
public sealed class NotificationConfig
{
    public const string SectionName = "Notifications";

    public int DueSoonDays { get; set; } = NotificationLimits.DefaultDueSoonDays;
    public string ScheduleTime { get; set; } = NotificationLimits.DefaultScheduleTime;
    public int RetentionDays { get; set; } = NotificationLimits.DefaultRetentionDays;

    /// <summary>Pekerja latar aktif. Tes mematikannya dan memanggil <c>NotificationJob</c> langsung.</summary>
    public bool SchedulerEnabled { get; set; } = true;
}

/// <summary>Seeder idempoten: satu baris pengaturan global dan satu baris per jenis katalog (tidak menimpa yang sudah ada).</summary>
public sealed class NotificationSeeder(GmsDbContext db, IOptions<NotificationConfig> config, TimeProvider time) : IDataSeeder
{
    public int Order => 40;

    public async Task SeedAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (!await db.NotificationOptionsRows.AnyAsync(ct))
        {
            var c = config.Value;
            db.NotificationOptionsRows.Add(new NotificationOptions
            {
                Id = 1,
                DueSoonDays = Math.Clamp(c.DueSoonDays, NotificationLimits.DueSoonDaysMin, NotificationLimits.DueSoonDaysMax),
                ScheduleTime = TimeOnly.TryParseExact(c.ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : new TimeOnly(6, 0),
                RetentionDays = Math.Clamp(c.RetentionDays, NotificationLimits.RetentionDaysMin, NotificationLimits.RetentionDaysMax),
                CreatedAt = now,
            });
        }

        var existing = await db.NotificationTypeSettings.Select(s => s.TypeCode).ToListAsync(ct);
        foreach (var def in NotificationCatalog.All.Where(d => !existing.Contains(d.TypeCode)))
        {
            db.NotificationTypeSettings.Add(new NotificationTypeSetting { TypeCode = def.TypeCode, Enabled = def.DefaultEnabled, CreatedAt = now });
        }

        await db.SaveChangesAsync(ct);
    }
}
