using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Gms.Api.Common;
using Gms.Domain.Notifications;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Enums;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gms.Api.Features.Notifications;

/// <summary>Hasil satu pemeriksaan (juga disimpan di <c>notification_job_runs.result</c>).</summary>
public sealed record NotificationJobOutcome(
    bool Skipped,
    IReadOnlyDictionary<string, int> Created,
    int EpisodesEnded,
    int RetentionDeleted,
    IReadOnlyList<string> Errors);

/// <summary>
/// Pemeriksaan Kondisi terjadwal dan retensi (ADR-016, FSD bagian 2.2 dan 4.3-4.5).
/// - Hanya satu pemeriksaan bersamaan: <c>pg_try_advisory_lock</c> pada koneksi khusus (instance lain melewati tanpa galat, AC-NT-21).
/// - Tiap jenis dalam transaksi dan try/catch sendiri (AC-NT-22).
/// - Episode: satu notifikasi per (jenis, objek, episode); unik parsial di database mencegah ganda antar instance (AC-NT-04).
/// - Definisi "terlambat" selaras widget Beranda (<c>DashboardQueries</c>).
/// </summary>
public sealed class NotificationJob(GmsDbContext db, AppTimeZone zone, TimeProvider time, ILogger<NotificationJob> logger)
{
    /// <summary>Kunci penasihat tetap ("GMSN" + 1).</summary>
    private const long AdvisoryKey = 0x474D534E01;

    private static readonly PoStatus[] ActivePo = [PoStatus.Confirmed, PoStatus.InProgress];
    private static readonly WoStatus[] OpenWo = [WoStatus.Draft, WoStatus.Released, WoStatus.InProgress];
    private const int RetentionBatch = 1000;
    private const int EndedEpisodeKeepDays = 400;

    /// <summary>Satu kondisi yang berlaku saat ini: kunci objek, parameter, dan tujuan.</summary>
    private sealed record Hit(string ObjectKey, Dictionary<string, string> Params, NotificationTarget Target);

    public async Task<NotificationJobOutcome> RunAsync(NotificationJobTrigger trigger, CancellationToken ct)
    {
        var started = time.GetUtcNow();
        var watch = Stopwatch.StartNew();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var locked = await TryLockAsync(ct);
            if (!locked)
            {
                return new NotificationJobOutcome(true, new Dictionary<string, int>(), 0, 0, []);
            }

            var created = new Dictionary<string, int>(StringComparer.Ordinal);
            var errors = new List<string>();
            var ended = 0;
            var deleted = 0;
            try
            {
                var options = await db.NotificationOptionsRows.AsNoTracking().SingleAsync(ct);
                var enabled = await db.NotificationTypeSettings.AsNoTracking().ToDictionaryAsync(s => s.TypeCode, s => s.Enabled, StringComparer.Ordinal, ct);
                var today = zone.Today(time);

                foreach (var def in NotificationCatalog.All.Where(d => d.Kind == NotificationKind.Condition))
                {
                    try
                    {
                        var isOn = enabled.GetValueOrDefault(def.TypeCode, def.DefaultEnabled);
                        var (c, e) = await ProcessConditionAsync(def, isOn, today, options.DueSoonDays, ct);
                        created[def.TypeCode] = c;
                        ended += e;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        db.ChangeTracker.Clear();
                        logger.LogError(ex, "Pemeriksaan notifikasi {TypeCode} gagal; dicoba lagi pada pemeriksaan berikutnya.", def.TypeCode);
                        errors.Add($"{def.TypeCode}: {ex.GetType().Name}");
                    }
                }

                try
                {
                    deleted = await PurgeAsync(options.RetentionDays, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Penghapusan retensi notifikasi gagal.");
                    errors.Add($"retention: {ex.GetType().Name}");
                }
            }
            finally
            {
                await UnlockAsync();
            }

            var outcome = new NotificationJobOutcome(false, created, ended, deleted, errors);
            db.ChangeTracker.Clear();
            db.NotificationJobRuns.Add(new NotificationJobRun
            {
                StartedAt = started,
                Trigger = trigger,
                Result = JsonSerializer.Serialize(new { created, episodesEnded = ended, retentionDeleted = deleted }),
                DurationMs = (int)watch.ElapsedMilliseconds,
                Error = errors.Count == 0 ? null : string.Join("; ", errors),
            });
            await db.SaveChangesAsync(ct);
            return outcome;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<bool> TryLockAsync(CancellationToken ct)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
        cmd.Parameters.Add(new NpgsqlParameter("k", AdvisoryKey));
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private async Task UnlockAsync()
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_unlock(@k)";
        cmd.Parameters.Add(new NpgsqlParameter("k", AdvisoryKey));
        await cmd.ExecuteScalarAsync(CancellationToken.None);
    }

    /// <summary>Satu jenis Kondisi dalam satu transaksi: akhiri episode yang kondisinya hilang, buka episode baru + notifikasi.</summary>
    private async Task<(int Created, int Ended)> ProcessConditionAsync(
        NotificationTypeDefinition def, bool enabled, DateOnly today, int dueSoonDays, CancellationToken ct)
    {
        var hits = (await QueryHitsAsync(def.TypeCode, today, dueSoonDays, ct)).ToDictionary(h => h.ObjectKey, StringComparer.Ordinal);
        var now = time.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var active = await db.NotificationEpisodes.Where(e => e.TypeCode == def.TypeCode && e.EndedAt == null).ToListAsync(ct);

        var ended = 0;
        foreach (var episode in active.Where(e => !hits.ContainsKey(e.ObjectKey)))
        {
            episode.EndedAt = now;
            ended++;
        }

        var created = 0;
        if (enabled)
        {
            var activeKeys = active.Select(e => e.ObjectKey).ToHashSet(StringComparer.Ordinal);
            var fresh = hits.Values.Where(h => !activeKeys.Contains(h.ObjectKey)).ToList();
            if (fresh.Count > 0)
            {
                var recipients = await NotificationRecipients.ResolveAsync(db, def, null, ct);
                foreach (var hit in fresh)
                {
                    var episode = new NotificationEpisode { TypeCode = def.TypeCode, ObjectKey = hit.ObjectKey, StartedAt = now };
                    db.NotificationEpisodes.Add(episode);
                    var payload = NotificationPublisher.BuildParams(def, hit.Params);
                    foreach (var recipient in recipients)
                    {
                        db.Notifications.Add(new Notification
                        {
                            RecipientUserId = recipient,
                            TypeCode = def.TypeCode,
                            Category = def.Category,
                            Params = payload,
                            TargetKind = hit.Target.Kind,
                            TargetId = hit.Target.Id,
                            SourceKey = $"{def.TypeCode}:{hit.ObjectKey}:{episode.Id}",
                            CreatedAt = now,
                        });
                    }

                    created += recipients.Count > 0 ? 1 : 0;
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Pemeriksa lain (instance lain) sudah membuka episode yang sama: tidak ada yang ganda; lewati jenis ini.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            logger.LogInformation("Episode {TypeCode} sudah dibuat pemeriksa lain; dilewati.", def.TypeCode);
            return (0, 0);
        }

        db.ChangeTracker.Clear();
        return (created, ended);
    }

    private async Task<List<Hit>> QueryHitsAsync(string typeCode, DateOnly today, int dueSoonDays, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (typeCode)
        {
            case "NC-01":
            {
                var rows = await (
                    from l in db.PoLines.AsNoTracking()
                    where l.DeliveryDate < today && l.DeliveredQty < l.Qty
                    join p in db.PurchaseOrders.AsNoTracking() on l.PoId equals p.Id
                    where ActivePo.Contains(p.Status)
                    select new { p.Id, p.Code }).ToListAsync(ct);
                return rows.GroupBy(r => (r.Id, r.Code)).Select(g => new Hit(
                    $"PO:{g.Key.Id}",
                    new Dictionary<string, string> { ["code"] = g.Key.Code, ["lines"] = g.Count().ToString(inv) },
                    new NotificationTarget(NotificationTargetKind.PurchaseOrder, g.Key.Id.ToString()))).ToList();
            }

            case "NC-02":
            {
                var until = today.AddDays(dueSoonDays);
                var rows = await (
                    from l in db.PoLines.AsNoTracking()
                    where l.DeliveryDate >= today && l.DeliveryDate <= until && l.DeliveredQty < l.Qty
                    join p in db.PurchaseOrders.AsNoTracking() on l.PoId equals p.Id
                    where ActivePo.Contains(p.Status)
                    select new { p.Id, p.Code, l.DeliveryDate, Open = l.Qty - l.DeliveredQty }).ToListAsync(ct);
                return rows.GroupBy(r => (r.Id, r.Code)).Select(g => new Hit(
                    $"PO:{g.Key.Id}",
                    new Dictionary<string, string>
                    {
                        ["code"] = g.Key.Code,
                        ["dueDate"] = g.Min(x => x.DeliveryDate).ToString("yyyy-MM-dd", inv),
                        ["qty"] = g.Sum(x => x.Open).ToString(inv),
                    },
                    new NotificationTarget(NotificationTargetKind.PurchaseOrder, g.Key.Id.ToString()))).ToList();
            }

            case "NC-03":
            {
                var rows = await db.WorkOrders.AsNoTracking()
                    .Where(w => w.TargetDate < today && OpenWo.Contains(w.Status))
                    .Select(w => new { w.Id, w.Code, w.TargetDate }).ToListAsync(ct);
                return rows.Select(w => new Hit(
                    $"WO:{w.Id}",
                    new Dictionary<string, string> { ["code"] = w.Code, ["targetDate"] = w.TargetDate!.Value.ToString("yyyy-MM-dd", inv) },
                    new NotificationTarget(NotificationTargetKind.WorkOrder, w.Id.ToString()))).ToList();
            }

            case "NC-04":
            {
                var rows = await db.PayrollPeriods.AsNoTracking()
                    .Where(p => p.Status == PayrollStatus.Open && p.EndDate < today)
                    .Select(p => new { p.Id, p.Code, p.EndDate }).ToListAsync(ct);
                return rows.Select(p => new Hit(
                    $"PAY:{p.Id}",
                    new Dictionary<string, string> { ["code"] = p.Code, ["endDate"] = p.EndDate.ToString("yyyy-MM-dd", inv) },
                    new NotificationTarget(NotificationTargetKind.PayrollPeriod, p.Id.ToString()))).ToList();
            }

            default:
                throw new InvalidOperationException($"Jenis kondisi tidak dikenal: {typeCode}");
        }
    }

    /// <summary>Retensi (AC-NT-15): notifikasi lebih tua dari masa simpan, termasuk yang belum dibaca; episode berakhir yang sudah lama.</summary>
    private async Task<int> PurgeAsync(int retentionDays, CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().AddDays(-retentionDays);
        var total = 0;
        while (true)
        {
            var batch = await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM notifications WHERE id IN (SELECT id FROM notifications WHERE created_at < {cutoff} LIMIT {RetentionBatch})", ct);
            total += batch;
            if (batch < RetentionBatch)
            {
                break;
            }
        }

        var episodeCutoff = time.GetUtcNow().AddDays(-EndedEpisodeKeepDays);
        await db.NotificationEpisodes.Where(e => e.EndedAt != null && e.EndedAt < episodeCutoff).ExecuteDeleteAsync(ct);
        return total;
    }
}
