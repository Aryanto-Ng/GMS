namespace Gms.Api.Security;

/// <summary>Konfigurasi bagian <c>Auth</c> (appsettings). Nilai bawaan mengikuti FSD Access Management.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Sesi berakhir bila tidak aktif selama ini (AC-AM-05).</summary>
    public int SessionTimeoutMinutes { get; set; } = 30;

    /// <summary>Jumlah gagal login berturut-turut sebelum akun terkunci (AC-AM-03).</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Pembaruan last_activity_at dibatasi paling sering sekali per interval ini.</summary>
    public int ActivityTouchSeconds { get; set; } = 60;
}

/// <summary>Konfigurasi bagian <c>Seed</c>. Hanya dipakai seeder pada database tanpa Administrator.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminUserName { get; set; } = "admin";
    public string AdminFullName { get; set; } = "Administrator";

    /// <summary>True = isi data contoh (master, FG dengan routing + tarif, dst.) untuk Development/UAT. Jangan di produksi.</summary>
    public bool DemoData { get; set; }

    /// <summary>Wajib bila belum ada Administrator. Jangan menaruh nilai di repository (user-secrets / environment).</summary>
    public string? AdminPassword { get; set; }
}

/// <summary>Konfigurasi bagian <c>App</c>.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    public string TimeZone { get; set; } = "Asia/Jakarta";

    /// <summary>Kop dokumen Comfi (surat jalan). Boleh kosong: PDF tetap dibuat (FSD Pengiriman E12).</summary>
    public CompanyOptions Comfi { get; set; } = new();

    /// <summary>Kop dokumen Konveksi (slip gaji).</summary>
    public CompanyOptions Konveksi { get; set; } = new();

    public PdfOptions Pdf { get; set; } = new();
}

public sealed class CompanyOptions
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

public sealed class PdfOptions
{
    /// <summary>
    /// Folder berisi <c>Regular.ttf</c> dan <c>Bold.ttf</c> (font berlisensi terbuka, mis. Liberation Sans) untuk Linux/Docker.
    /// Di Windows font sistem dipakai. Relatif terhadap folder aplikasi bila tidak absolut.
    /// </summary>
    public string FontDirectory { get; set; } = "Fonts";
}
