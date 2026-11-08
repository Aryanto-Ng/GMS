using Microsoft.Extensions.Options;

namespace Gms.Api.Common;

/// <summary>
/// Zona waktu aplikasi (ADR-010, default Asia/Jakarta). Dipakai untuk "hari ini" dan batas tanggal bisnis.
/// Jika zona tidak ditemukan di sistem (mis. image tanpa tzdata) jatuh ke UTC+7 tetap.
/// </summary>
public sealed class AppTimeZone
{
    private readonly TimeZoneInfo _zone;

    public AppTimeZone(IOptions<Security.AppOptions> options)
    {
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            _zone = TimeZoneInfo.CreateCustomTimeZone("WIB", TimeSpan.FromHours(7), "WIB", "WIB");
        }
    }

    public DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);

    /// <summary>Awal hari (00:00 lokal) sebagai instan UTC.</summary>
    public DateTimeOffset StartOfDayUtc(DateOnly date) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _zone), TimeSpan.Zero);
}
