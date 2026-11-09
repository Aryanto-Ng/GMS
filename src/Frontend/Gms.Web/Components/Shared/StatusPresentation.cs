using Gms.Shared.Enums;
using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace Gms.Web.Components.Shared;

/// <summary>Tampilan satu status: label, nada warna (kelas CSS), ikon, dan tooltip. Pemetaan tunggal agar seluruh modul konsisten.</summary>
public sealed record StatusPresentation(string Label, string Tone, Icon Icon, string? Tooltip = null, bool Strike = false)
{
    private static Icon Edit => new Icons.Regular.Size16.Edit();
    private static Icon Check => new Icons.Regular.Size16.CheckmarkCircle();
    private static Icon Progress => new Icons.Regular.Size16.ArrowSync();
    private static Icon Done => new Icons.Regular.Size16.CheckmarkUnderlineCircle();
    private static Icon Lock => new Icons.Regular.Size16.LockClosed();
    private static Icon Cancel => new Icons.Regular.Size16.DismissCircle();
    private static Icon Clock => new Icons.Regular.Size16.Clock();
    private static Icon Info => new Icons.Regular.Size16.Info();
    private static Icon Warning => new Icons.Regular.Size16.Warning();

    public static StatusPresentation For(object? value) => value switch
    {
        PoStatus s => ForPo(s),
        WoStatus s => ForWo(s),
        UserStatus.Active => new("Aktif", "success", Check),
        UserStatus.Inactive => new("Nonaktif", "neutral", Cancel),
        UserStatus.Locked => new("Terkunci", "danger", Lock, "Terlalu banyak percobaan login. Buka kunci atau tunggu hingga waktu berakhir."),
        DocStatus.Active => new("Aktif", "success", Check),
        DocStatus.Voided => new("Voided", "neutral", Cancel, "Dokumen dibatalkan; tidak dihitung.", Strike: true),
        PayrollStatus.Open => new("Open", "info", Info),
        PayrollStatus.Closed => new("Closed", "dark", Lock, "Periode ditutup dan tidak dapat dibuka kembali."),
        "late" => new("Terlambat", "danger", Clock),
        "provisional" => new("Belum Final", "warning", Info, "Angka dihitung langsung dan masih dapat berubah."),
        "ready" => new("Siap Produksi", "success", Check),
        "not-ready" => new("Belum Siap", "warning", Warning),
        "active" => new("Aktif", "success", Check),
        "inactive" => new("Nonaktif", "neutral", Cancel),
        _ => new(value?.ToString() ?? "—", "neutral", Info),
    };

    private static StatusPresentation ForPo(PoStatus status) => status switch
    {
        PoStatus.Draft => new("Draft", "neutral", Edit),
        PoStatus.Confirmed => new("Confirmed", "info", Check),
        PoStatus.InProgress => new("In Progress", "brand", Progress),
        PoStatus.Completed => new("Completed", "success", Done),
        _ => new("Cancelled", "danger-soft", Cancel),
    };

    private static StatusPresentation ForWo(WoStatus status) => status switch
    {
        WoStatus.Draft => new("Draft", "neutral", Edit),
        WoStatus.Released => new("Released", "info", Check),
        WoStatus.InProgress => new("In Progress", "brand", Progress),
        WoStatus.Completed => new("Completed", "success", Done),
        WoStatus.Closed => new("Closed", "dark", Lock),
        _ => new("Cancelled", "danger-soft", Cancel),
    };
}
