using System.Globalization;
using System.Text.RegularExpressions;
using Gms.Shared.Notifications;
using Gms.Web.Infrastructure.Format;
using Gms.Web.Resources;
using Microsoft.Extensions.Localization;

namespace Gms.Web.Services.Notifications;

/// <summary>Teks tampil satu notifikasi: judul, keterangan, dan badge jenis (bila bermakna, memakai peta warna Design.md 8).</summary>
public sealed record NotificationView(string Title, string Body, string? BadgeLabel, string? BadgeTone);

/// <summary>
/// Merakit kalimat notifikasi dari kunci resource <c>{TypeCode}.Title/Body</c> + parameter (server tidak mengirim teks jadi, FSD AC-NT-12).
/// Bahasa mengikuti culture UI aktif (ID/EN); tanggal dan angka mengikuti culture (Design.md 12). Dilarang menyusun kalimat dari potongan string:
/// satu kunci = satu kalimat utuh dengan token <c>{nama}</c>.
/// </summary>
public sealed partial class NotificationText(IStringLocalizer<NotificationResource> loc)
{
    private static readonly string[] DateKeys = ["workDate", "dueDate", "targetDate", "endDate"];
    private static readonly string[] NumberKeys = ["qty", "allocatedQty", "freeQty", "releasedQty", "lines", "attempts"];

    private static bool English => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    public NotificationView For(NotificationItem item)
    {
        var p = new Dictionary<string, string>(item.Params, StringComparer.Ordinal);
        if (p.TryGetValue("action", out var action))
        {
            p["actionText"] = loc[$"Action.{action}"].Value;
        }

        var title = Fill(loc[$"{item.TypeCode}.Title"].Value, p);
        var body = Fill(loc[$"{item.TypeCode}.Body"].Value, p);

        var badge = item.TypeCode is "NE-05" or "NE-06" or "NE-07" && p.TryGetValue("action", out var a)
            ? loc[$"Badge.{a}"].Value
            : loc[$"{item.TypeCode}.Badge"].Value;
        if (string.IsNullOrWhiteSpace(badge) || badge == $"{item.TypeCode}.Badge")
        {
            return new NotificationView(title, body, null, null);
        }

        return new NotificationView(title, body, badge, ToneFor(item.TypeCode));
    }

    /// <summary>Kelas warna badge menurut peta status (merah hanya untuk keterlambatan/galat; void dan batal abu-abu).</summary>
    public static string? ToneFor(string typeCode) => typeCode switch
    {
        "NE-01" => "t-proc",
        "NE-02" or "NE-04" => "t-ok",
        "NE-05" or "NE-06" or "NE-07" or "NE-08" or "NE-12" => "t-obs",
        "NE-09" or "NC-01" or "NC-03" => "t-err",
        "NE-11" or "NC-02" => "t-warn",
        "NC-04" => "t-prog",
        _ => null,
    };

    public string Relative(DateTimeOffset at, DateTimeOffset now)
    {
        var age = now - at;
        if (age < TimeSpan.FromMinutes(1))
        {
            return loc["Time.Now"].Value;
        }

        if (age < TimeSpan.FromHours(1))
        {
            return string.Format(CultureInfo.CurrentUICulture, loc["Time.Minutes"].Value, (int)age.TotalMinutes);
        }

        if (age < TimeSpan.FromHours(24))
        {
            return string.Format(CultureInfo.CurrentUICulture, loc["Time.Hours"].Value, (int)age.TotalHours);
        }

        return age < TimeSpan.FromHours(48) ? loc["Time.Yesterday"].Value : FormatDate(at);
    }

    public static string FormatDate(DateTimeOffset at) => English
        ? at.ToOffset(IdFormat.Offset).ToString("MMM d, yyyy HH:mm", CultureInfo.GetCultureInfo("en-US"))
        : IdFormat.DateTime(at);

    private string Fill(string template, Dictionary<string, string> p) =>
        TokenRegex().Replace(template, m => p.TryGetValue(m.Groups[1].Value, out var v) ? FormatParam(m.Groups[1].Value, v) : string.Empty);

    private static string FormatParam(string key, string value)
    {
        if (DateKeys.Contains(key) && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return English ? date.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US")) : IdFormat.Date(date);
        }

        if (key == "at" && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return FormatDate(at);
        }

        if (NumberKeys.Contains(key) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return English ? number.ToString("N0", CultureInfo.GetCultureInfo("en-US")) : IdFormat.Qty(number);
        }

        return value;
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex TokenRegex();
}

/// <summary>Tujuan klik notifikasi → rute halaman (di-open sebagai tab oleh Tab Manager).</summary>
public static class NotificationLinks
{
    public static string UrlFor(NotificationTarget? target)
    {
        if (target is null)
        {
            return "/";
        }

        return target.Kind switch
        {
            NotificationTargetKind.PurchaseOrder => $"/purchase-orders/{target.Id}",
            NotificationTargetKind.WorkOrder => $"/work-orders/{target.Id}",
            NotificationTargetKind.FgHandover => $"/handovers/fg/{target.Id}",
            NotificationTargetKind.Delivery => $"/deliveries/{target.Id}",
            NotificationTargetKind.PayrollPeriod => $"/payroll/{target.Id}",
            NotificationTargetKind.OutputEntry => "/production/history",
            NotificationTargetKind.StockLedger => StockLedger(target.Id),
            NotificationTargetKind.UserList => "/admin/users",
            _ => "/",
        };
    }

    private static string StockLedger(string id)
    {
        var parts = id.Split(':');
        return parts.Length == 3 ? $"/stock/ledger?fgId={parts[0]}&colorId={parts[1]}&sizeId={parts[2]}" : "/stock";
    }
}
