using Gms.Shared.Master;
using Gms.Web.Infrastructure.Format;

namespace Gms.Web.Components.Pages.Master.FgParts;

/// <summary>Logika murni grid tarif: penguraian sel (tidak membulatkan diam-diam), pengisian satu baris, dan sel belum terisi.</summary>
public static class TariffLogic
{
    public const decimal MaxRate = 999_999_999m;

    /// <summary>Teks kosong = belum diisi (null, tanpa galat). Selain itu harus angka Indonesia &gt; 0 dengan maksimal 5 desimal.</summary>
    public static (decimal? Rate, string? Error) ParseCell(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        if (!IdNumber.TryParseDecimal(text, out var value, out var error))
        {
            return (null, error);
        }

        if (value <= 0)
        {
            return (null, "Harus lebih dari 0.");
        }

        if (decimal.Round(value, 5) != value)
        {
            return (null, "Maksimal 5 digit desimal.");
        }

        return value > MaxRate ? (null, "Terlalu besar.") : (value, null);
    }

    public static string FormatCell(decimal? rate) => rate is { } r ? IdFormat.Amount(r) : string.Empty;

    /// <summary>Nilai sel pertama yang terisi (valid) pada satu baris, untuk "Isi semua".</summary>
    public static decimal? FirstFilled(IEnumerable<string?> rowTexts) =>
        rowTexts.Select(ParseCell).Where(p => p.Rate is not null).Select(p => p.Rate).FirstOrDefault();

    /// <summary>true bila mengisi semua sel baris dengan <paramref name="value"/> akan menimpa isian lain yang berbeda.</summary>
    public static bool WouldOverwrite(IEnumerable<string?> rowTexts, decimal value) =>
        rowTexts.Any(t => !string.IsNullOrWhiteSpace(t) && ParseCell(t).Rate != value);

    /// <summary>Sel (langkah × size) yang belum terisi menurut urutan baris lalu kolom.</summary>
    public static IReadOnlyList<(RoutingStepDto Step, Gms.Shared.Common.SizeRef Size)> Missing(
        FgTariffGridDto grid, Func<Guid, Guid, string?> text) =>
        [.. from step in grid.InternalSteps from size in grid.Sizes where string.IsNullOrWhiteSpace(text(step.ProcessId, size.Id)) select (step, size)];
}
