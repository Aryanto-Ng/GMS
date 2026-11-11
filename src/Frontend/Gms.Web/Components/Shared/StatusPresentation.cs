using Gms.Shared.Enums;

namespace Gms.Web.Components.Shared;

/// <summary>
/// Tampilan satu status (Design.md 4.1 dan 8.2): label, nada (kelas CSS), tipe ikon AntDesign, dan tooltip. Pemetaan tunggal agar seluruh modul konsisten.
/// Nada: neutral (Draft/Nonaktif/Void), cyan (Confirmed/Released), blue (In Progress/Open), success (Completed/Aktif/Siap),
/// closed (terkunci), warning (Jatuh tempo/Belum final/Belum siap), danger (Cancelled/Terlambat/Terkunci).
/// </summary>
public sealed record StatusPresentation(string Label, string Tone, string Icon, string? Tooltip = null, bool Strike = false)
{
    public static StatusPresentation For(object? value) => value switch
    {
        PoStatus s => ForPo(s),
        WoStatus s => ForWo(s),
        UserStatus.Active => new("Aktif", "success", "check"),
        UserStatus.Inactive => new("Nonaktif", "neutral", "minus-circle"),
        UserStatus.Locked => new("Terkunci", "danger", "lock", "Terlalu banyak percobaan login. Buka kunci atau tunggu hingga waktu berakhir."),
        DocStatus.Active => new("Aktif", "success", "check"),
        DocStatus.Voided => new("Voided", "neutral", "minus-circle", "Dokumen dibatalkan; tidak dihitung.", Strike: true),
        PayrollStatus.Open => new("Open", "blue", "unlock"),
        PayrollStatus.Closed => new("Closed", "closed", "lock", "Periode ditutup dan tidak dapat dibuka kembali."),
        "late" => new("Terlambat", "danger", "clock-circle"),
        "provisional" => new("Belum Final", "warning", "warning", "Angka dihitung langsung dan masih dapat berubah."),
        "ready" => new("Siap Produksi", "success", "check-circle"),
        "not-ready" => new("Belum Siap", "warning", "warning"),
        "active" => new("Aktif", "success", "check"),
        "inactive" => new("Nonaktif", "neutral", "minus-circle"),
        _ => new(value?.ToString() ?? "—", "neutral", "info-circle"),
    };

    private static StatusPresentation ForPo(PoStatus status) => status switch
    {
        PoStatus.Draft => new("Draft", "neutral", "edit"),
        PoStatus.Confirmed => new("Confirmed", "cyan", "flag"),
        PoStatus.InProgress => new("In Progress", "blue", "sync"),
        PoStatus.Completed => new("Completed", "success", "check-circle"),
        _ => new("Cancelled", "danger", "stop"),
    };

    private static StatusPresentation ForWo(WoStatus status) => status switch
    {
        WoStatus.Draft => new("Draft", "neutral", "edit"),
        WoStatus.Released => new("Released", "cyan", "send"),
        WoStatus.InProgress => new("In Progress", "blue", "sync"),
        WoStatus.Completed => new("Completed", "success", "check-circle"),
        WoStatus.Closed => new("Closed", "closed", "lock"),
        _ => new("Cancelled", "danger", "stop"),
    };
}
