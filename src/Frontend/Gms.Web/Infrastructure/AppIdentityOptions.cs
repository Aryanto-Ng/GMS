namespace Gms.Web.Infrastructure;

/// <summary>
/// Identitas aplikasi (Design.md 2): satu-satunya nilai yang berganti per proyek. Dibind dari <c>appsettings.json</c> bagian <c>AppIdentity</c>;
/// tidak ada nama proyek yang tertulis keras di komponen shell.
/// </summary>
public sealed class AppIdentityOptions
{
    public const string SectionName = "AppIdentity";

    public string Name { get; set; } = "Garment Management System";

    public string Abbreviation { get; set; } = "GMS";

    public string LogoLetter { get; set; } = "G";

    public string Tagline { get; set; } = "Manajemen produksi, stok, dan pengiriman";

    public string Company { get; set; } = string.Empty;

    public int Year { get; set; } = 2026;

    public string Footer => $"© {Year} {Name}. Hak cipta dilindungi.";
}
