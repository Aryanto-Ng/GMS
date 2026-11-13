using Gms.Domain.Common;
using Gms.Shared.Notifications;

namespace Gms.Domain.Notifications;

/// <summary>
/// Satu notifikasi untuk satu penerima (fan-out saat dibuat, ADR-016). Tanpa teks jadi: kalimat dirakit klien dari
/// <see cref="TypeCode"/> + <see cref="Params"/>. Tidak boleh memuat tarif atau rupiah.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RecipientUserId { get; set; }

    /// <summary>NE-xx atau NC-xx (<see cref="NotificationCatalog"/>).</summary>
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>Disimpan agar chip filter dan hitung per kategori cukup satu query.</summary>
    public NotificationCategory Category { get; set; }

    /// <summary>jsonb: objek datar string→string (nilai Invariant).</summary>
    public string Params { get; set; } = "{}";

    public NotificationTargetKind? TargetKind { get; set; }
    public string? TargetId { get; set; }

    /// <summary>Pelaku (Event); dikecualikan dari penerima. Null untuk Kondisi.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Kunci idempotensi per penerima. Event: <c>{TypeCode}:{objek}:{pemicu}</c> (mis. id entri ledger).
    /// Kondisi: <c>{TypeCode}:{ObjectKey}:{episodeId}</c>. Unik bersama <see cref="RecipientUserId"/>.
    /// </summary>
    public string SourceKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null = belum dibaca.</summary>
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>
/// Episode sebuah kondisi terjadwal pada satu objek. Aktif selama <see cref="EndedAt"/> null; unik per
/// (<see cref="TypeCode"/>, <see cref="ObjectKey"/>) yang aktif. Dipisah dari tabel notifikasi agar retensi tidak
/// memicu notifikasi ulang (FSD AC-NT-16).
/// </summary>
public sealed class NotificationEpisode
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>Mis. <c>PO:{id}</c>, <c>WO:{id}</c>, <c>PAY:{id}</c>.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>Pengaktifan per jenis (satu baris per <see cref="NotificationCatalog"/>; di-seed idempoten).</summary>
public sealed class NotificationTypeSetting : VersionedEntity
{
    public string TypeCode { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>Pengaturan global (tepat satu baris, <c>Id = 1</c>).</summary>
public sealed class NotificationOptions : VersionedEntity
{
    public int Id { get; set; } = 1;
    public int DueSoonDays { get; set; } = NotificationLimits.DefaultDueSoonDays;

    /// <summary>Jam pemeriksaan harian pada zona waktu aplikasi.</summary>
    public TimeOnly ScheduleTime { get; set; } = new(6, 0);

    public int RetentionDays { get; set; } = NotificationLimits.DefaultRetentionDays;
}

public enum NotificationJobTrigger
{
    Scheduled,
    Startup,
}

/// <summary>Catatan satu pemeriksaan pekerja latar (AC-NT-22, AC-NT-23).</summary>
public sealed class NotificationJobRun
{
    public long Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public NotificationJobTrigger Trigger { get; set; }

    /// <summary>jsonb: jumlah notifikasi dibuat per TypeCode, episode berakhir, baris retensi dihapus.</summary>
    public string Result { get; set; } = "{}";

    public int DurationMs { get; set; }
    public string? Error { get; set; }
}

/// <summary>Peristiwa yang diterbitkan modul sumber (jenis Event) di dalam transaksi aksinya.</summary>
public sealed record NotificationEvent(
    string TypeCode,
    string SourceKey,
    IReadOnlyDictionary<string, string> Params,
    NotificationTarget Target,
    Guid? ActorUserId);

/// <summary>
/// Abstraksi tunggal untuk modul sumber. Implementasi (Gms.Api) menentukan penerima dari <see cref="NotificationCatalog"/>
/// (pengguna aktif berizin, bukan pelaku), memeriksa pengaturan jenis, lalu MENAMBAHKAN entitas ke DbContext yang sama
/// sehingga atomik dengan aksi bisnis (AC-NT-03). Tidak boleh mengunci baris bisnis dan tidak boleh memanggil SaveChanges.
/// </summary>
public interface INotificationPublisher
{
    Task PublishAsync(NotificationEvent evt, CancellationToken ct = default);
}
