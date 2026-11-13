using Gms.Shared.Common;
using Gms.Shared.Security;

namespace Gms.Shared.Notifications;

/// <summary>Jenis pemicu (FSD Notifikasi bagian 2): Kejadian = dipicu aksi pengguna, Kondisi = diperiksa pekerja terjadwal.</summary>
public enum NotificationKind
{
    Event,
    Condition,
}

/// <summary>Kategori untuk chip filter dan pengelompokan (UI: Notifikasi-UI.md bagian 3).</summary>
public enum NotificationCategory
{
    Produksi,
    Penjualan,
    Gudang,
    Penggajian,
    Keamanan,
}

/// <summary>
/// Jenis dokumen tujuan klik. Klien memetakannya ke rute/tab (ID tab: _Foundation-UI.md bagian 4.1).
/// <c>TargetId</c> berisi id dokumen (GUID string) kecuali dinyatakan lain.
/// </summary>
public enum NotificationTargetKind
{
    PurchaseOrder,
    WorkOrder,
    FgHandover,
    Delivery,
    PayrollPeriod,
    OutputEntry,

    /// <summary><c>TargetId</c> = <c>{fgId}:{colorId}:{sizeId}</c>.</summary>
    StockLedger,

    /// <summary>Daftar pengguna terfilter terkunci; <c>TargetId</c> = id pengguna.</summary>
    UserList,
}

public sealed record NotificationTarget(NotificationTargetKind Kind, string Id);

/// <summary>
/// Definisi satu jenis notifikasi. Judul dan isi TIDAK disimpan sebagai teks: klien merakit kalimat dari kunci resource
/// <c>Notif.{TypeCode}.Title</c> dan <c>Notif.{TypeCode}.Body</c> memakai <see cref="ParamKeys"/> (FSD AC-NT-12).
/// </summary>
public sealed record NotificationTypeDefinition(
    string TypeCode,
    NotificationKind Kind,
    NotificationCategory Category,
    string Name,
    IReadOnlyList<string> RequiredPermissions,
    NotificationTargetKind TargetKind,
    IReadOnlyList<string> ParamKeys,
    bool DefaultEnabled = true);

/// <summary>
/// Katalog tetap 16 jenis notifikasi (FSD Notifikasi bagian 2). Sumber kebenaran untuk server (penentuan penerima),
/// layar Pengaturan (nama dan izin penerima), dan tes konsistensi.
/// Aturan penerima: pengguna aktif yang memiliki SEMUA izin pada <see cref="NotificationTypeDefinition.RequiredPermissions"/>,
/// kecuali pelaku (jenis Event).
/// </summary>
public static class NotificationCatalog
{
    private const NotificationKind E = NotificationKind.Event;
    private const NotificationKind C = NotificationKind.Condition;

    public static IReadOnlyList<NotificationTypeDefinition> All { get; } =
    [
        new("NE-01", E, NotificationCategory.Produksi, "WO dirilis", [Permissions.ProductionOutput.Create], NotificationTargetKind.WorkOrder, ["code", "fg", "customer"]),
        new("NE-02", E, NotificationCategory.Produksi, "WO selesai, menunggu Close", [Permissions.WorkOrder.Close], NotificationTargetKind.WorkOrder, ["code"]),
        new("NE-03", E, NotificationCategory.Gudang, "Serah Terima FG dibuat", [Permissions.StokFG.View], NotificationTargetKind.FgHandover, ["wo", "qty", "allocatedQty", "freeQty"]),
        new("NE-04", E, NotificationCategory.Penggajian, "Periode gaji ditutup", [Permissions.Payroll.View], NotificationTargetKind.PayrollPeriod, ["code"]),
        new("NE-05", E, NotificationCategory.Produksi, "Void/Koreksi Output", [Permissions.ProductionOutput.View], NotificationTargetKind.OutputEntry, ["action", "wo", "process", "worker", "workDate", "qty"]),
        new("NE-06", E, NotificationCategory.Gudang, "Void/Koreksi Serah Terima FG", [Permissions.SerahTerimaFG.View], NotificationTargetKind.FgHandover, ["action", "wo", "qty"]),
        new("NE-07", E, NotificationCategory.Penjualan, "Void/Koreksi Pengiriman", [Permissions.Pengiriman.View], NotificationTargetKind.Delivery, ["action", "code", "po"]),
        new("NE-08", E, NotificationCategory.Penjualan, "PO dibatalkan", [Permissions.CustomerPO.View], NotificationTargetKind.PurchaseOrder, ["code", "releasedQty"]),
        new("NE-09", E, NotificationCategory.Keamanan, "Akun terkunci", [Permissions.AccessManagement.AuditLog.View], NotificationTargetKind.UserList, ["username", "attempts"]),
        new("NE-10", E, NotificationCategory.Keamanan, "Sesi diakhiri paksa", [Permissions.AccessManagement.AuditLog.View], NotificationTargetKind.UserList, ["username", "at"]),
        new("NE-11", E, NotificationCategory.Gudang, "Stok tersedia habis", [Permissions.StokFG.View], NotificationTargetKind.StockLedger, ["fg", "color", "size"]),
        new("NE-12", E, NotificationCategory.Penjualan, "Alokasi dilepas otomatis", [Permissions.CustomerPO.View], NotificationTargetKind.PurchaseOrder, ["po", "qty", "reason"]),
        new("NC-01", C, NotificationCategory.Penjualan, "PO terlambat", [Permissions.CustomerPO.View], NotificationTargetKind.PurchaseOrder, ["code", "lines"]),
        new("NC-02", C, NotificationCategory.Penjualan, "PO jatuh tempo", [Permissions.CustomerPO.View], NotificationTargetKind.PurchaseOrder, ["code", "dueDate", "qty"]),
        new("NC-03", C, NotificationCategory.Produksi, "WO terlambat", [Permissions.WorkOrder.View], NotificationTargetKind.WorkOrder, ["code", "targetDate"]),
        new("NC-04", C, NotificationCategory.Penggajian, "Periode gaji perlu ditutup", [Permissions.Payroll.View], NotificationTargetKind.PayrollPeriod, ["code", "endDate"]),
    ];

    public static NotificationTypeDefinition? Find(string typeCode) =>
        All.FirstOrDefault(x => string.Equals(x.TypeCode, typeCode, StringComparison.Ordinal));
}

/// <summary>Batas nilai pengaturan global (FSD AC-NT-17).</summary>
public static class NotificationLimits
{
    public const int DueSoonDaysMin = 1;
    public const int DueSoonDaysMax = 14;
    public const int RetentionDaysMin = 30;
    public const int RetentionDaysMax = 365;
    public const int DefaultDueSoonDays = 3;
    public const int DefaultRetentionDays = 90;
    public const string DefaultScheduleTime = "06:00";

    /// <summary>Jumlah item pada panel lonceng (AC-NT-08).</summary>
    public const int PanelSize = 20;
}

// ---------------------------------------------------------------- Notifikasi pengguna

/// <summary>
/// Satu notifikasi milik pengguna yang sedang login. <see cref="Params"/> berisi nilai terformat Invariant
/// (tanggal ISO <c>yyyy-MM-dd</c>, angka titik desimal); klien memformatnya sesuai culture aktif. Tanpa tarif/rupiah.
/// </summary>
public sealed record NotificationItem(
    Guid Id,
    string TypeCode,
    NotificationCategory Category,
    IReadOnlyDictionary<string, string> Params,
    NotificationTarget? Target,
    DateTimeOffset CreatedAt,
    bool IsRead);

public sealed record NotificationListQuery : PageQuery
{
    public NotificationCategory? Category { get; init; }

    /// <summary><c>true</c> = hanya belum dibaca.</summary>
    public bool? Unread { get; init; }
}

/// <summary>Jumlah untuk chip filter (hasil hitung server; izin sumber sudah diterapkan, AC-NT-13).</summary>
public sealed record NotificationCounts(
    int All,
    int Unread,
    IReadOnlyDictionary<NotificationCategory, int> ByCategory);

/// <summary>Jawaban endpoint polling ringan (lencana lonceng).</summary>
public sealed record NotificationSummary(int UnreadCount);

public sealed record MarkAllReadResult(int Marked);

/// <summary>Klien bertipe notifikasi pengguna (ADR-014: ada <c>HttpNotificationsApi</c> dan <c>MockNotificationsApi</c>).</summary>
public interface INotificationsApi
{
    Task<NotificationSummary> GetSummaryAsync(CancellationToken ct = default);

    Task<PagedResult<NotificationItem>> ListAsync(NotificationListQuery query, CancellationToken ct = default);

    Task<NotificationCounts> GetCountsAsync(string? q = null, CancellationToken ct = default);

    /// <summary>Menandai satu notifikasi milik pemanggil terbaca. Milik pengguna lain = 404 (AC-NT-14).</summary>
    Task MarkReadAsync(Guid id, CancellationToken ct = default);

    Task<MarkAllReadResult> MarkAllReadAsync(CancellationToken ct = default);
}

// ---------------------------------------------------------------- Pengaturan (Administrator)

public sealed record NotificationTypeSettingDto(string TypeCode, bool Enabled);

/// <summary>
/// Pengaturan notifikasi. Nama jenis, kategori, dan izin penerima diambil klien dari <see cref="NotificationCatalog"/>
/// (tidak diulang pada DTO). <see cref="ScheduleTime"/> format <c>HH:mm</c> pada zona waktu aplikasi.
/// </summary>
public sealed record NotificationSettingsDto(
    int DueSoonDays,
    string ScheduleTime,
    int RetentionDays,
    IReadOnlyList<NotificationTypeSettingDto> Types,
    uint RowVersion,
    DateTimeOffset? UpdatedAt,
    string? UpdatedByName);

public sealed record UpdateNotificationSettingsRequest(
    int DueSoonDays,
    string ScheduleTime,
    int RetentionDays,
    IReadOnlyList<NotificationTypeSettingDto> Types,
    uint RowVersion);

/// <summary>Izin: <c>Notifikasi.Pengaturan.View</c> (baca) dan <c>.Edit</c> (ubah).</summary>
public interface INotificationSettingsApi
{
    Task<NotificationSettingsDto> GetAsync(CancellationToken ct = default);

    /// <summary>Validasi: batas <see cref="NotificationLimits"/>, <c>HH:mm</c> valid, semua <c>TypeCode</c> dikenal. 400 dengan <c>violations[]</c> bila gagal; 409 bila <c>RowVersion</c> usang.</summary>
    Task<NotificationSettingsDto> UpdateAsync(UpdateNotificationSettingsRequest request, CancellationToken ct = default);
}
