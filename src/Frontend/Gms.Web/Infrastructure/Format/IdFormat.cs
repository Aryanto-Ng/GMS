using System.Globalization;

namespace Gms.Web.Infrastructure.Format;

/// <summary>
/// Format tampilan Indonesia (_Foundation-UI.md bagian 2). Ditulis eksplisit (nama bulan, pemisah ribuan titik, desimal koma) agar
/// tidak bergantung pada ICU/budaya sistem: image Docker sering memakai globalization-invariant.
/// </summary>
public static class IdFormat
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des"];

    private static readonly NumberFormatInfo Numbers = new()
    {
        NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NegativeSign = "−", // tanda minus tipografis (qty negatif)
        NumberGroupSizes = [3],
    };

    /// <summary>Zona waktu tampilan (WIB, UTC+7). Konfigurasi aplikasi menggantinya bila perlu.</summary>
    public static TimeSpan Offset { get; set; } = TimeSpan.FromHours(7);

    public static string Date(DateOnly value) => $"{value.Day:00} {Months[value.Month - 1]} {value.Year}";

    public static string Date(DateOnly? value) => value is { } v ? Date(v) : "—";

    /// <summary>Bentuk isian tanggal: <c>dd/MM/yyyy</c>.</summary>
    public static string DateInput(DateOnly value) => $"{value.Day:00}/{value.Month:00}/{value.Year}";

    public static string DateTime(DateTimeOffset value)
    {
        var local = value.ToOffset(Offset);
        return $"{local.Day:00} {Months[local.Month - 1]} {local.Year} {local.Hour:00}:{local.Minute:00}";
    }

    public static string DateTime(DateTimeOffset? value) => value is { } v ? DateTime(v) : "—";

    public static string Time(DateTimeOffset value) => value.ToOffset(Offset).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Qty bulat: <c>12.500</c>, negatif <c>−20</c>.</summary>
    public static string Qty(int value) => value.ToString("#,##0", Numbers);

    public static string Qty(int? value) => value is { } v ? Qty(v) : "—";

    public static string Number(decimal value, int decimals = 0) => value.ToString("#,##0" + (decimals > 0 ? "." + new string('#', decimals) : string.Empty), Numbers);

    /// <summary>Rupiah tanpa desimal untuk total: <c>Rp 1.250.000</c>. Negatif: <c>−Rp 20.000</c>.</summary>
    public static string Rupiah(decimal value)
    {
        var rounded = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        return rounded < 0 ? $"−Rp {Math.Abs(rounded).ToString("#,##0", Numbers)}" : $"Rp {rounded.ToString("#,##0", Numbers)}";
    }

    /// <summary>Tarif hingga 5 desimal tanpa nol ekor: <c>Rp 1.250,5</c>.</summary>
    public static string Rate(decimal value)
    {
        var text = Math.Abs(value).ToString("#,##0.#####", Numbers);
        return value < 0 ? $"−Rp {text}" : $"Rp {text}";
    }

    /// <summary>Jumlah tanpa pembulatan hingga 5 desimal (rincian upah), tanpa simbol mata uang.</summary>
    public static string Amount(decimal value) => value.ToString("#,##0.#####", Numbers);

    public static string Percent(decimal value, int decimals = 0) => $"{value.ToString("0" + (decimals > 0 ? "." + new string('#', decimals) : string.Empty), Numbers)}%";
}

/// <summary>
/// Pengurai angka masukan format Indonesia (koma desimal, titik ribuan). Masukan ambigu DITOLAK dengan petunjuk, tidak ditebak
/// atau dibulatkan diam-diam (mis. "1.5" bisa berarti 1,5 atau 15).
/// </summary>
public static class IdNumber
{
    public static bool TryParseInt(string? text, out int value, out string? error)
    {
        value = 0;
        if (!TryParseDecimal(text, out var number, out error))
        {
            return false;
        }

        if (number != decimal.Truncate(number))
        {
            error = "Isi bilangan bulat tanpa desimal.";
            return false;
        }

        if (number is > int.MaxValue or < int.MinValue)
        {
            error = "Angka terlalu besar.";
            return false;
        }

        value = (int)number;
        return true;
    }

    public static bool TryParseDecimal(string? text, out decimal value, out string? error)
    {
        value = 0;
        error = null;
        var s = (text ?? string.Empty).Trim().Replace("Rp", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty).Replace("−", "-");
        if (s.Length == 0)
        {
            error = "Isi angka.";
            return false;
        }

        var negative = s.StartsWith('-');
        if (negative)
        {
            s = s[1..];
        }

        if (s.Length == 0 || s.Any(c => !char.IsDigit(c) && c is not ('.' or ',')))
        {
            error = "Hanya angka, titik ribuan, dan koma desimal yang diperbolehkan.";
            return false;
        }

        var commaCount = s.Count(c => c == ',');
        if (commaCount > 1)
        {
            error = "Gunakan satu koma untuk desimal.";
            return false;
        }

        var commaIndex = s.IndexOf(',');
        var integerPart = commaIndex >= 0 ? s[..commaIndex] : s;
        var fraction = commaIndex >= 0 ? s[(commaIndex + 1)..] : string.Empty;

        if (integerPart.Contains('.'))
        {
            var groups = integerPart.Split('.');
            var validGrouping = groups[0].Length is >= 1 and <= 3 && groups.Skip(1).All(g => g.Length == 3);
            if (!validGrouping)
            {
                // "1.5" / "12.34": titik bukan pemisah ribuan yang sah, dan titik bukan pemisah desimal di format Indonesia.
                error = "Gunakan koma untuk desimal (mis. 1,5) dan titik hanya untuk ribuan (mis. 1.500).";
                return false;
            }

            integerPart = string.Concat(groups);
        }

        if (fraction.Contains('.'))
        {
            error = "Gunakan koma untuk desimal.";
            return false;
        }

        if (integerPart.Length == 0)
        {
            integerPart = "0";
        }

        if (!decimal.TryParse($"{integerPart}.{(fraction.Length == 0 ? "0" : fraction)}", NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            error = "Angka tidak valid.";
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }

    /// <summary>Tanggal isian <c>dd/MM/yyyy</c> (atau <c>yyyy-MM-dd</c>).</summary>
    public static bool TryParseDate(string? text, out DateOnly value) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim(), ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
