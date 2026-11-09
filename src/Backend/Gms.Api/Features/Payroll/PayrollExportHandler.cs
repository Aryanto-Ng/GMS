using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Payroll;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Payroll;

/// <summary>
/// Ekspor rekap periode (AC-PY-25..27): Excel (4 sheet, angka sebagai angka) dan CSV (ringkasan atau harian, UTF-8 BOM, koma, titik desimal).
/// Periode Open diberi "BELUM FINAL" pada nama berkas dan isi. Pembulatan rupiah hanya pada kolom Dibayarkan.
/// </summary>
public sealed class PayrollExportHandler(GmsDbContext db, PayrollQueries queries, ICurrentUser user, IAuditWriter audit)
{
    private const string RateFormat = "#,##0.#####";
    private const string MoneyFormat = "#,##0";
    private const string DateFormat = "dd-mm-yyyy";

    public async Task<FileContent> HandleAsync(Guid periodId, PayrollExportFormat format, CancellationToken ct)
    {
        var (period, lines) = await queries.LoadAsync(periodId, ct);
        var provisional = period.Status == PayrollStatus.Open;
        var suffix = provisional ? "-BELUM-FINAL" : string.Empty;
        var baseName = $"payroll-{period.Code}{suffix}";

        var file = format switch
        {
            PayrollExportFormat.Xlsx => new FileContent($"{baseName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", BuildWorkbook(period, lines, provisional)),
            PayrollExportFormat.CsvSummary => new FileContent($"{baseName}-ringkasan.csv", "text/csv", BuildSummaryCsv(lines, provisional)),
            _ => new FileContent($"{baseName}-harian.csv", "text/csv", BuildDailyCsv(lines, provisional)),
        };

        audit.Add(AuditEvents.PayrollExported, "PayrollPeriod", periodId.ToString(), null,
            new { period.Code, format = format.ToString(), provisional, by = user.UserName });
        await db.SaveChangesAsync(ct);
        return file;
    }

    // ===== Excel =====

    private static byte[] BuildWorkbook(PayrollPeriod period, List<PayrollLine> lines, bool provisional)
    {
        using var workbook = new XLWorkbook();
        var title = $"Rekap Upah Borongan {period.Code} ({period.StartDate:dd-MM-yyyy} s.d. {period.EndDate:dd-MM-yyyy}) — {(provisional ? "BELUM FINAL" : "FINAL")}";

        var summary = workbook.AddWorksheet("Ringkasan");
        Title(summary, title, 5);
        Header(summary, 3, "Kode", "Nama", "Total Qty", "Jumlah (sebelum pembulatan)", "Dibayarkan (dibulatkan)");
        var totals = PayrollCalculator.Totals(lines);
        var row = 4;
        foreach (var t in totals)
        {
            summary.Cell(row, 1).Value = t.WorkerCode;
            summary.Cell(row, 2).Value = t.WorkerName;
            summary.Cell(row, 3).Value = t.TotalQty;
            Money(summary.Cell(row, 4), t.SumBeforeRounding, "#,##0.#####");
            Money(summary.Cell(row, 5), t.Paid, MoneyFormat);
            row++;
        }

        summary.Cell(row, 2).Value = $"Total ({totals.Count} pekerja)";
        summary.Cell(row, 3).Value = totals.Sum(t => t.TotalQty);
        Money(summary.Cell(row, 4), totals.Sum(t => t.SumBeforeRounding), "#,##0.#####");
        Money(summary.Cell(row, 5), totals.Sum(t => t.Paid), MoneyFormat);
        summary.Range(row, 1, row, 5).Style.Font.Bold = true;
        Finish(summary);

        var detail = workbook.AddWorksheet("Rincian");
        Title(detail, title, 8);
        Header(detail, 3, "Kode Pekerja", "Nama", "WO", "Proses", "Size", "Qty", "Tarif", "Subtotal");
        row = 4;
        foreach (var g in lines.GroupBy(l => (l.WorkerCode, l.WorkerName, l.WoCode, l.ProcessCode, l.ProcessName, l.SizeCode, l.SizeSort, l.Rate))
                     .OrderBy(g => g.Key.WorkerCode, StringComparer.Ordinal).ThenBy(g => g.Key.WoCode, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.ProcessCode, StringComparer.Ordinal).ThenBy(g => g.Key.SizeSort))
        {
            detail.Cell(row, 1).Value = g.Key.WorkerCode;
            detail.Cell(row, 2).Value = g.Key.WorkerName;
            detail.Cell(row, 3).Value = g.Key.WoCode;
            detail.Cell(row, 4).Value = g.Key.ProcessName;
            detail.Cell(row, 5).Value = g.Key.SizeCode;
            detail.Cell(row, 6).Value = g.Sum(l => l.Qty);
            Money(detail.Cell(row, 7), g.Key.Rate, RateFormat);
            Money(detail.Cell(row, 8), g.Sum(l => l.Subtotal), "#,##0.#####");
            row++;
        }

        Finish(detail);
        DailySheet(workbook.AddWorksheet("Harian"), title, lines);
        DailySheet(workbook.AddWorksheet("Penyesuaian"), title, lines.Where(l => l.Kind == OutputKind.Adjustment).ToList());

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void DailySheet(IXLWorksheet sheet, string title, List<PayrollLine> lines)
    {
        Title(sheet, title, 12);
        Header(sheet, 3, "Tanggal Kerja", "Kode Pekerja", "Nama", "WO", "Proses", "Size", "Jenis", "Qty", "Tarif", "Subtotal", "Tanggal Asal", "Alasan");
        var row = 4;
        foreach (var l in lines)
        {
            sheet.Cell(row, 1).Value = l.WorkDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = DateFormat;
            sheet.Cell(row, 2).Value = l.WorkerCode;
            sheet.Cell(row, 3).Value = l.WorkerName;
            sheet.Cell(row, 4).Value = l.WoCode;
            sheet.Cell(row, 5).Value = l.ProcessName;
            sheet.Cell(row, 6).Value = l.SizeCode;
            sheet.Cell(row, 7).Value = l.Kind == OutputKind.Adjustment ? "Penyesuaian" : "Output";
            sheet.Cell(row, 8).Value = l.Qty;
            Money(sheet.Cell(row, 9), l.Rate, RateFormat);
            Money(sheet.Cell(row, 10), l.Subtotal, "#,##0.#####");
            if (l.OriginalDate is { } original)
            {
                sheet.Cell(row, 11).Value = original.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(row, 11).Style.DateFormat.Format = DateFormat;
            }

            sheet.Cell(row, 12).Value = l.Reason ?? string.Empty;
            row++;
        }

        Finish(sheet);
    }

    private static void Title(IXLWorksheet sheet, string title, int columns)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Range(1, 1, 1, columns).Merge().Style.Font.Bold = true;
    }

    private static void Header(IXLWorksheet sheet, int row, params string[] headers)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(row, i + 1).Value = headers[i];
        }

        var range = sheet.Range(row, 1, row, headers.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    private static void Money(IXLCell cell, decimal value, string format)
    {
        cell.Value = value; // disimpan sebagai angka, bukan teks
        cell.Style.NumberFormat.Format = format;
    }

    private static void Finish(IXLWorksheet sheet)
    {
        sheet.SheetView.FreezeRows(3);
        sheet.Columns().AdjustToContents(3, 60);
    }

    // ===== CSV =====

    private static string Text(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text; // cegah injeksi rumus saat dibuka di spreadsheet
        }

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static string Num(decimal value) => value.ToString("0.#####", CultureInfo.InvariantCulture);

    private static byte[] Csv(IEnumerable<string> rows) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", rows) + "\r\n")).ToArray();

    private static byte[] BuildSummaryCsv(List<PayrollLine> lines, bool provisional)
    {
        var status = provisional ? "BELUM FINAL" : "FINAL";
        var rows = new List<string> { "kode_pekerja,nama,total_qty,jumlah_sebelum_pembulatan,dibayarkan,status" };
        rows.AddRange(PayrollCalculator.Totals(lines).Select(t =>
            $"{Text(t.WorkerCode)},{Text(t.WorkerName)},{t.TotalQty},{Num(t.SumBeforeRounding)},{Num(t.Paid)},{status}"));
        return Csv(rows);
    }

    private static byte[] BuildDailyCsv(List<PayrollLine> lines, bool provisional)
    {
        var status = provisional ? "BELUM FINAL" : "FINAL";
        var rows = new List<string> { "tanggal_kerja,kode_pekerja,nama,wo,proses,size,jenis,qty,tarif,subtotal,tanggal_asal,alasan,status" };
        rows.AddRange(lines.Select(l =>
            $"{l.WorkDate:yyyy-MM-dd},{Text(l.WorkerCode)},{Text(l.WorkerName)},{Text(l.WoCode)},{Text(l.ProcessName)},{Text(l.SizeCode)}," +
            $"{(l.Kind == OutputKind.Adjustment ? "Penyesuaian" : "Output")},{l.Qty},{Num(l.Rate)},{Num(l.Subtotal)}," +
            $"{(l.OriginalDate is { } o ? o.ToString("yyyy-MM-dd") : string.Empty)},{Text(l.Reason)},{status}"));
        return Csv(rows);
    }
}
