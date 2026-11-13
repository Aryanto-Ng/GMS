using Microsoft.Extensions.Localization;

namespace Gms.Web.Resources;

/// <summary>
/// Penanda sumber daya teks chrome aplikasi (K-2): header, navigasi, tab, footer, label form masuk, dan nama status turunan.
/// Bawaan = Indonesia (<c>ChromeResource.resx</c>), English = <c>ChromeResource.en.resx</c>. Nama status dokumen (Draft, Confirmed, In Progress, ...)
/// tetap Inggris di kedua bahasa (FSD, Design.md 12).
/// </summary>
public sealed class ChromeResource;

public static class LocalizerExtensions
{
    /// <summary>Teks terlokalisasi, atau <paramref name="fallback"/> bila kunci tidak ada (mis. entri katalog baru yang belum diterjemahkan).</summary>
    public static string Or(this IStringLocalizer localizer, string key, string fallback)
    {
        var value = localizer[key];
        return value.ResourceNotFound ? fallback : value.Value;
    }
}
