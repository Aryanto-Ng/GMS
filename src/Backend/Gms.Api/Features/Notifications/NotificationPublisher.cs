using System.Text.Json;
using Gms.Domain.Notifications;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Notifications;

/// <summary>
/// Penerbit jenis Kejadian (ADR-016). Dipanggil handler sumber SEBELUM <c>SaveChanges</c> sehingga notifikasi atomik dengan aksi bisnis:
/// hanya membaca pengaturan dan penerima lalu <c>Add</c> ke DbContext yang sama. TIDAK mengunci baris bisnis dan TIDAK menyimpan sendiri.
/// Kegagalan non-database saat menentukan penerima dicatat dan tidak menggagalkan aksi (AC-NT-03).
/// </summary>
public sealed class NotificationPublisher(GmsDbContext db, TimeProvider time, ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    private const int MaxParamLength = 200;

    public async Task PublishAsync(NotificationEvent evt, CancellationToken ct = default)
    {
        var def = NotificationCatalog.Find(evt.TypeCode)
            ?? throw new ArgumentException($"Jenis notifikasi tidak dikenal: {evt.TypeCode}", nameof(evt));
        if (def.Kind != NotificationKind.Event)
        {
            throw new ArgumentException($"{evt.TypeCode} adalah jenis Kondisi, bukan Kejadian.", nameof(evt));
        }

        List<Guid> recipients;
        try
        {
            var enabled = await db.NotificationTypeSettings.AsNoTracking()
                .Where(s => s.TypeCode == def.TypeCode).Select(s => (bool?)s.Enabled).FirstOrDefaultAsync(ct);
            if (!(enabled ?? def.DefaultEnabled))
            {
                return;
            }

            recipients = await NotificationRecipients.ResolveAsync(db, def, evt.ActorUserId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not NpgsqlOrDbException)
        {
            logger.LogError(ex, "Gagal menentukan penerima notifikasi {TypeCode}; aksi bisnis tetap dilanjutkan.", def.TypeCode);
            return;
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var payload = BuildParams(def, evt.Params);
        var now = time.GetUtcNow();
        foreach (var recipient in recipients)
        {
            db.Notifications.Add(new Notification
            {
                RecipientUserId = recipient,
                TypeCode = def.TypeCode,
                Category = def.Category,
                Params = payload,
                TargetKind = evt.Target.Kind,
                TargetId = Truncate(evt.Target.Id, 120),
                ActorUserId = evt.ActorUserId,
                SourceKey = Truncate(evt.SourceKey, 200),
                CreatedAt = now,
            });
        }
    }

    /// <summary>Hanya kunci yang didefinisikan katalog yang lolos (tidak ada tarif/rupiah yang dapat masuk). Nilai dipangkas.</summary>
    public static string BuildParams(NotificationTypeDefinition def, IReadOnlyDictionary<string, string> source)
    {
        var filtered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in def.ParamKeys)
        {
            if (source.TryGetValue(key, out var value) && value is not null)
            {
                filtered[key] = Truncate(value, MaxParamLength);
            }
        }

        return JsonSerializer.Serialize(filtered);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Marker agar pengecualian database (yang meracuni transaksi) tidak ditelan.</summary>
    private abstract class NpgsqlOrDbException : Exception;
}
