using Gms.Api.Common;
using Gms.Domain.Notifications;
using Gms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Notifications;

/// <summary>
/// Pekerja latar (ADR-016 butir 7). Tiap 5 menit memeriksa apakah pemeriksaan harian perlu dijalankan:
/// jam terjadwal (zona aplikasi) sudah lewat dan belum ada pemeriksaan sejak jam itu hari ini. Karena memakai pencatatan
/// <c>notification_job_runs</c> (bukan timer absolut), instance yang tertidur melewati jam terjadwal akan menyusul saat bangun.
/// Saat startup: jalankan hanya bila pemeriksaan terakhir lebih dari 24 jam lalu (AC-NT-23).
/// </summary>
public sealed class NotificationScheduler(IServiceScopeFactory scopes, AppTimeZone zone, TimeProvider time, ILogger<NotificationScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupMaxAge = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Beri waktu migrasi/seeder startup selesai.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var first = true;
        using var timer = new PeriodicTimer(Tick);
        do
        {
            try
            {
                await TickAsync(first, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Tick pekerja notifikasi gagal.");
            }

            first = false;
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task TickAsync(bool startup, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GmsDbContext>();
        var options = await db.NotificationOptionsRows.AsNoTracking().FirstOrDefaultAsync(ct);
        if (options is null)
        {
            return; // belum di-seed
        }

        var now = time.GetUtcNow();
        var lastRun = await db.NotificationJobRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Select(r => (DateTimeOffset?)r.StartedAt).FirstOrDefaultAsync(ct);

        var due = false;
        var trigger = NotificationJobTrigger.Scheduled;
        if (startup && (lastRun is null || now - lastRun > StartupMaxAge))
        {
            due = true;
            trigger = NotificationJobTrigger.Startup;
        }
        else if (IsDue(now, lastRun, options.ScheduleTime))
        {
            due = true;
        }

        if (!due)
        {
            return;
        }

        var job = scope.ServiceProvider.GetRequiredService<NotificationJob>();
        var outcome = await job.RunAsync(trigger, ct);
        if (!outcome.Skipped)
        {
            logger.LogInformation(
                "Pemeriksaan notifikasi ({Trigger}) selesai: dibuat {Created}, episode berakhir {Ended}, retensi {Deleted}, galat {Errors}.",
                trigger, outcome.Created.Values.Sum(), outcome.EpisodesEnded, outcome.RetentionDeleted, outcome.Errors.Count);
        }
    }

    /// <summary>True bila jam terjadwal hari ini (zona aplikasi) sudah lewat dan belum ada pemeriksaan sejak saat itu.</summary>
    internal bool IsDue(DateTimeOffset now, DateTimeOffset? lastRun, TimeOnly schedule)
    {
        var today = zone.Today(time);
        var scheduledUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(schedule, DateTimeKind.Unspecified), zone.Zone), TimeSpan.Zero);
        return now >= scheduledUtc && (lastRun is null || lastRun < scheduledUtc);
    }
}
