using Gms.Shared.Enums;

namespace Gms.Web.Components.Shared;

/// <summary>
/// Tampilan satu status (Design.md 8): label, kelas warna badge, ikon opsional, tooltip. Pemetaan TUNGGAL untuk seluruh modul.
/// Kelas: t-proc (biru: Draft, Confirmed, Released, Belum Mulai), t-prog (kuning: In Progress, Open, Berjalan),
/// t-ok (hijau: Completed, Closed, Selesai, Aktif, Siap), t-obs (abu: Cancelled, Void, Nonaktif),
/// t-err (merah: Terlambat, Terkunci, Negatif), t-warn (oranye: Jatuh tempo, Belum final, Belum siap).
/// Merah hanya untuk keterlambatan dan galat; Cancelled/Void BUKAN merah.
/// </summary>
public sealed record StatusPresentation(string Label, string Tone, string? Icon, string? Tooltip = null, bool Strike = false)
{
    public static StatusPresentation For(object? value) => value switch
    {
        PoStatus s => ForPo(s),
        WoStatus s => ForWo(s),
        UserStatus.Active => new("Aktif", "t-ok", "check"),
        UserStatus.Inactive => new("Nonaktif", "t-obs", "ban"),
        UserStatus.Locked => new("Terkunci", "t-err", "lock", "Terlalu banyak percobaan login. Buka kunci atau tunggu hingga waktu berakhir."),
        DocStatus.Active => new("Aktif", "t-ok", "check"),
        DocStatus.Voided => new("Voided", "t-obs", "ban", "Dokumen dibatalkan; tidak dihitung.", Strike: true),
        PayrollStatus.Open => new("Open", "t-prog", null),
        PayrollStatus.Closed => new("Closed", "t-ok", "lock", "Periode ditutup dan tidak dapat dibuka kembali."),
        "late" => new("Terlambat", "t-err", "clock"),
        "due-soon" => new("Jatuh tempo", "t-warn", "alert"),
        "no-number" => new("Belum ada", "t-warn", "alert"),
        "negative" => new("Negatif", "t-err", "alert"),
        "provisional" => new("Belum Final", "t-warn", "alert", "Angka dihitung langsung dan masih dapat berubah."),
        "ready" => new("Siap Produksi", "t-ok", "check-circle"),
        "not-ready" => new("Belum Siap", "t-warn", "alert"),
        "active" => new("Aktif", "t-ok", "check"),
        "inactive" => new("Nonaktif", "t-obs", "ban"),
        "step-not-started" => new("Belum Mulai", "t-proc", null),
        "step-running" => new("Berjalan", "t-prog", null),
        "step-done" => new("Selesai", "t-ok", "check-circle"),
        _ => new(value?.ToString() ?? "—", "t-obs", null),
    };

    private static StatusPresentation ForPo(PoStatus status) => status switch
    {
        PoStatus.Draft => new("Draft", "t-proc", null),
        PoStatus.Confirmed => new("Confirmed", "t-proc", null),
        PoStatus.InProgress => new("In Progress", "t-prog", null),
        PoStatus.Completed => new("Completed", "t-ok", "check-circle"),
        _ => new("Cancelled", "t-obs", "ban"),
    };

    private static StatusPresentation ForWo(WoStatus status) => status switch
    {
        WoStatus.Draft => new("Draft", "t-proc", null),
        WoStatus.Released => new("Released", "t-proc", null),
        WoStatus.InProgress => new("In Progress", "t-prog", null),
        WoStatus.Completed => new("Completed", "t-ok", "check-circle"),
        WoStatus.Closed => new("Closed", "t-ok", "lock"),
        _ => new("Cancelled", "t-obs", "ban"),
    };
}
