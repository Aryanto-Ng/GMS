namespace Gms.Web.Components.Pages.Admin;

/// <summary>Label manusiawi peristiwa audit (kode dari server, mis. <c>LoginSucceeded</c>); kode tak dikenal ditampilkan apa adanya.</summary>
public static class AuditLabels
{
    public static readonly IReadOnlyList<(string Code, string Label)> Known =
    [
        ("LoginSucceeded", "Login berhasil"), ("LoginFailed", "Login gagal"), ("AccountLocked", "Akun terkunci"), ("Logout", "Logout"),
        ("SessionReplaced", "Sesi digantikan"), ("SessionEnded", "Sesi diakhiri"), ("PasswordChanged", "Password diganti"), ("PasswordReset", "Password direset"),
        ("UserCreated", "Pengguna dibuat"), ("UserUpdated", "Pengguna diubah"), ("UserDeactivated", "Pengguna dinonaktifkan"), ("UserActivated", "Pengguna diaktifkan"),
        ("UserUnlocked", "Kunci dibuka"), ("RoleCreated", "Role dibuat"), ("RoleUpdated", "Role diubah"), ("RoleDeleted", "Role dihapus"), ("AccessDenied", "Akses ditolak"),
    ];

    private static readonly Dictionary<string, string> Map = Known.ToDictionary(k => k.Code, k => k.Label);

    public static string For(string code) => Map.TryGetValue(code, out var label) ? label : code;
}
