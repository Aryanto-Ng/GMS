using Gms.Shared.Problems;
using PdfSharp.Fonts;

namespace Gms.Api.Documents;

/// <summary>
/// Penyedia font untuk PDF bersama (surat jalan, slip gaji). Windows memakai font sistem; Linux/Docker membaca
/// <c>Regular.ttf</c> dan <c>Bold.ttf</c> dari folder font aplikasi (font berlisensi terbuka disediakan DevOps).
/// Satu keluarga font ("Arial" sebagai nama logis) dipetakan ke dua berkas tersebut agar tampilan sama di semua platform.
/// </summary>
public static class PdfFonts
{
    public const string Family = "Arial";

    private static readonly object Gate = new();
    private static bool _initialized;

    public static void EnsureInitialized(string? fontDirectory)
    {
        if (_initialized)
        {
            return;
        }

        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            }
            else
            {
                var directory = Path.IsPathRooted(fontDirectory ?? string.Empty)
                    ? fontDirectory!
                    : Path.Combine(AppContext.BaseDirectory, string.IsNullOrWhiteSpace(fontDirectory) ? "Fonts" : fontDirectory);
                var regular = Path.Combine(directory, "Regular.ttf");
                var bold = Path.Combine(directory, "Bold.ttf");
                if ((!File.Exists(regular) || !File.Exists(bold)) && FindSystemFonts() is { } system)
                {
                    (regular, bold) = system; // cadangan: font sistem Linux (paket fonts-liberation / DejaVu)
                }

                if (!File.Exists(regular) || !File.Exists(bold))
                {
                    throw new Gms.Api.Common.ProblemException(StatusCodes.Status503ServiceUnavailable, ProblemTypes.ServiceUnavailable,
                        "Font PDF belum tersedia.", $"Letakkan Regular.ttf dan Bold.ttf di folder font ({directory}) lalu coba lagi.");
                }

                GlobalFontSettings.FontResolver = new FileFontResolver(File.ReadAllBytes(regular), File.ReadAllBytes(bold));
            }

            _initialized = true;
        }
    }

    private static (string Regular, string Bold)? FindSystemFonts()
    {
        (string, string)[] candidates =
        [
            ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"),
            ("/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf"),
            ("/usr/share/fonts/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/liberation/LiberationSans-Bold.ttf"),
            ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
        ];
        foreach (var (regular, bold) in candidates)
        {
            if (File.Exists(regular) && File.Exists(bold))
            {
                return (regular, bold);
            }
        }

        return null;
    }

    private sealed class FileFontResolver(byte[] regular, byte[] bold) : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? "gms-bold" : "gms-regular");

        public byte[]? GetFont(string faceName) => faceName == "gms-bold" ? bold : regular;
    }
}
