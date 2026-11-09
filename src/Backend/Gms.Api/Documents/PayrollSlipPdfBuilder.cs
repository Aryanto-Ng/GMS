using System.Globalization;
using Gms.Shared.Payroll;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Gms.Api.Documents;

public sealed record SlipWorker(
    string WorkerCode, string WorkerName, int TotalQty, decimal SumBeforeRounding, decimal Paid,
    IReadOnlyList<PayrollWoGroup> ByWo, IReadOnlyList<PayrollDayGroup> ByDay, IReadOnlyList<PayrollDailyRow> Adjustments);

public sealed record SlipModel(
    string PeriodCode, DateOnly Start, DateOnly End, bool Provisional, string CompanyName, string CompanyAddress,
    string PrintedBy, DateTime PrintedAtLocal, bool IncludeDaily, IReadOnlyList<SlipWorker> Workers);

/// <summary>
/// Slip Upah Borongan A4 (AC-PY-28..32): satu pekerja per halaman, rincian WO → Proses → Size, penyesuaian dengan Tanggal Asal,
/// jumlah sebelum pembulatan dan dibayarkan, rincian harian opsional, kolom tanda terima, watermark BELUM FINAL untuk periode Open.
/// Angka diformat gaya Indonesia (titik ribuan, koma desimal) tanpa bergantung pada data budaya sistem.
/// </summary>
public static class PayrollSlipPdfBuilder
{
    private static readonly NumberFormatInfo Id = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NegativeSign = "-" };
    private static readonly Color Muted = new(90, 90, 90);
    private static readonly Color HeaderFill = new(235, 235, 235);

    public static string Money(decimal value) => value.ToString("#,##0", Id);

    public static string Amount(decimal value) => value.ToString("#,##0.#####", Id);

    public static string Rate(decimal value) => value.ToString("#,##0.#####", Id);

    public static byte[] Build(SlipModel model, string? fontDirectory)
    {
        PdfFonts.EnsureInitialized(fontDirectory);
        var document = CreateDocument(model);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var pdf = renderer.PdfDocument;
        pdf.Info.Title = $"Slip Upah Borongan {model.PeriodCode}";
        pdf.Info.Author = model.CompanyName;
        pdf.Info.Subject = model.Provisional ? "BELUM FINAL" : "FINAL";

        if (model.Provisional)
        {
            foreach (var page in pdf.Pages)
            {
                DrawWatermark(page, "BELUM FINAL");
            }
        }

        using var stream = new MemoryStream();
        pdf.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void DrawWatermark(PdfPage page, string text)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var font = new XFont(PdfFonts.Family, 70, XFontStyleEx.Bold);
        gfx.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
        gfx.RotateTransform(-45);
        gfx.DrawString(text, font, new XSolidBrush(XColor.FromArgb(60, 200, 120, 0)), new XPoint(0, 0), XStringFormats.Center);
    }

    private static Document CreateDocument(SlipModel m)
    {
        var doc = new Document();
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = PdfFonts.Family;
        normal.Font.Size = 9;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        var width = Unit.FromCentimeter(21 - 3.6);

        var footer = section.Footers.Primary.AddParagraph($"Dicetak oleh {m.PrintedBy} pada {m.PrintedAtLocal:dd-MM-yyyy HH:mm} WIB");
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        footer.Format.Alignment = ParagraphAlignment.Right;

        for (var i = 0; i < m.Workers.Count; i++)
        {
            if (i > 0)
            {
                section.AddPageBreak(); // satu pekerja mulai di halaman baru
            }

            AddSlip(section, m, m.Workers[i], width);
        }

        if (m.Workers.Count == 0)
        {
            section.AddParagraph("Tidak ada data upah pada periode ini.");
        }

        return doc;
    }

    private static void AddSlip(Section section, SlipModel m, SlipWorker w, Unit width)
    {
        var company = section.AddParagraph(string.IsNullOrWhiteSpace(m.CompanyName) ? " " : m.CompanyName);
        company.Format.Font.Size = 14;
        company.Format.Font.Bold = true;
        if (!string.IsNullOrWhiteSpace(m.CompanyAddress))
        {
            var address = section.AddParagraph(m.CompanyAddress);
            address.Format.Font.Size = 8.5;
            address.Format.Font.Color = Muted;
        }

        var title = section.AddParagraph("SLIP UPAH BORONGAN");
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.Font.Size = 15;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromCentimeter(0.3);
        title.Format.SpaceAfter = Unit.FromCentimeter(0.3);
        title.Format.Borders.Top.Width = 0.75;
        title.Format.Borders.Bottom.Width = 0.75;

        Field(section, "Periode", $"{m.PeriodCode} ({m.Start:dd-MM-yyyy} s.d. {m.End:dd-MM-yyyy}){(m.Provisional ? " — BELUM FINAL" : string.Empty)}");
        Field(section, "Pekerja", $"{w.WorkerCode} — {w.WorkerName}");
        section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);

        // WO → Proses → Size
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Muted;
        table.AddColumn(width - Unit.FromCentimeter(9.2));
        table.AddColumn(Unit.FromCentimeter(1.8));
        table.AddColumn(Unit.FromCentimeter(2.0));
        table.AddColumn(Unit.FromCentimeter(2.4));
        table.AddColumn(Unit.FromCentimeter(3.0));
        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = HeaderFill;
        header.Format.Font.Bold = true;
        Cell(header.Cells[0], "WO · Proses", ParagraphAlignment.Left);
        Cell(header.Cells[1], "Size", ParagraphAlignment.Center);
        Cell(header.Cells[2], "Qty", ParagraphAlignment.Right);
        Cell(header.Cells[3], "Tarif", ParagraphAlignment.Right);
        Cell(header.Cells[4], "Subtotal", ParagraphAlignment.Right);

        foreach (var group in w.ByWo)
        {
            for (var i = 0; i < group.Lines.Count; i++)
            {
                var line = group.Lines[i];
                var row = table.AddRow();
                Cell(row.Cells[0], i == 0 ? $"{group.WoCode} · {group.ProcessName}" : string.Empty, ParagraphAlignment.Left);
                Cell(row.Cells[1], line.SizeCode, ParagraphAlignment.Center);
                Cell(row.Cells[2], line.Qty.ToString("#,##0", Id), ParagraphAlignment.Right);
                Cell(row.Cells[3], Rate(line.Rate), ParagraphAlignment.Right);
                Cell(row.Cells[4], Amount(line.Subtotal), ParagraphAlignment.Right);
            }
        }

        if (w.Adjustments.Count > 0)
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);
            var heading = section.AddParagraph("Penyesuaian (periode sebelumnya)");
            heading.Format.Font.Bold = true;
            var adj = section.AddTable();
            adj.Borders.Width = 0.5;
            adj.Borders.Color = Muted;
            adj.AddColumn(Unit.FromCentimeter(2.2));
            adj.AddColumn(Unit.FromCentimeter(2.2));
            adj.AddColumn(width - Unit.FromCentimeter(2.2 + 2.2 + 1.8 + 3.0 + 3.2));
            adj.AddColumn(Unit.FromCentimeter(1.8));
            adj.AddColumn(Unit.FromCentimeter(3.0));
            adj.AddColumn(Unit.FromCentimeter(3.2));
            var h = adj.AddRow();
            h.Shading.Color = HeaderFill;
            h.Format.Font.Bold = true;
            Cell(h.Cells[0], "Tgl Kerja", ParagraphAlignment.Left);
            Cell(h.Cells[1], "Tgl Asal", ParagraphAlignment.Left);
            Cell(h.Cells[2], "WO · Proses · Size", ParagraphAlignment.Left);
            Cell(h.Cells[3], "Qty", ParagraphAlignment.Right);
            Cell(h.Cells[4], "Subtotal", ParagraphAlignment.Right);
            Cell(h.Cells[5], "Alasan", ParagraphAlignment.Left);
            foreach (var a in w.Adjustments)
            {
                var r = adj.AddRow();
                Cell(r.Cells[0], a.WorkDate.ToString("dd-MM-yyyy"), ParagraphAlignment.Left);
                Cell(r.Cells[1], a.OriginalDate?.ToString("dd-MM-yyyy") ?? "-", ParagraphAlignment.Left);
                Cell(r.Cells[2], $"{a.WoCode} · {a.ProcessName} · {a.SizeCode}", ParagraphAlignment.Left);
                Cell(r.Cells[3], a.Qty.ToString("#,##0", Id), ParagraphAlignment.Right);
                Cell(r.Cells[4], Amount(a.Subtotal), ParagraphAlignment.Right);
                Cell(r.Cells[5], a.Reason ?? "-", ParagraphAlignment.Left);
            }
        }

        if (m.IncludeDaily)
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);
            var heading = section.AddParagraph("Rincian Harian");
            heading.Format.Font.Bold = true;
            var daily = section.AddTable();
            daily.Borders.Width = 0.5;
            daily.Borders.Color = Muted;
            daily.AddColumn(Unit.FromCentimeter(2.4));
            daily.AddColumn(width - Unit.FromCentimeter(2.4 + 1.6 + 2.0 + 2.4 + 3.0));
            daily.AddColumn(Unit.FromCentimeter(1.6));
            daily.AddColumn(Unit.FromCentimeter(2.0));
            daily.AddColumn(Unit.FromCentimeter(2.4));
            daily.AddColumn(Unit.FromCentimeter(3.0));
            var h = daily.AddRow();
            h.Shading.Color = HeaderFill;
            h.Format.Font.Bold = true;
            Cell(h.Cells[0], "Tanggal", ParagraphAlignment.Left);
            Cell(h.Cells[1], "WO · Proses", ParagraphAlignment.Left);
            Cell(h.Cells[2], "Size", ParagraphAlignment.Center);
            Cell(h.Cells[3], "Qty", ParagraphAlignment.Right);
            Cell(h.Cells[4], "Tarif", ParagraphAlignment.Right);
            Cell(h.Cells[5], "Subtotal", ParagraphAlignment.Right);
            foreach (var day in w.ByDay)
            {
                foreach (var r in day.Rows)
                {
                    var row = daily.AddRow();
                    Cell(row.Cells[0], day.WorkDate.ToString("dd-MM-yyyy"), ParagraphAlignment.Left);
                    Cell(row.Cells[1], $"{r.WoCode} · {r.ProcessName}", ParagraphAlignment.Left);
                    Cell(row.Cells[2], r.SizeCode, ParagraphAlignment.Center);
                    Cell(row.Cells[3], r.Qty.ToString("#,##0", Id), ParagraphAlignment.Right);
                    Cell(row.Cells[4], Rate(r.Rate), ParagraphAlignment.Right);
                    Cell(row.Cells[5], Amount(r.Subtotal), ParagraphAlignment.Right);
                }

                var sub = daily.AddRow();
                sub.Shading.Color = HeaderFill;
                Cell(sub.Cells[0], $"Subtotal {day.WorkDate:dd-MM-yyyy}", ParagraphAlignment.Left, bold: true);
                sub.Cells[0].MergeRight = 4;
                Cell(sub.Cells[5], Amount(day.Subtotal), ParagraphAlignment.Right, bold: true);
            }
        }

        // Total
        section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.3);
        var totals = section.AddTable();
        totals.AddColumn(width - Unit.FromCentimeter(7.5));
        totals.AddColumn(Unit.FromCentimeter(7.5));
        TotalRow(totals, "Total Qty", w.TotalQty.ToString("#,##0", Id));
        TotalRow(totals, "Jumlah (sebelum pembulatan)", $"Rp {Amount(w.SumBeforeRounding)}");
        TotalRow(totals, "Dibayarkan (dibulatkan)", $"Rp {Money(w.Paid)}", bold: true, size: 12);
        if (w.Paid < 0 || w.SumBeforeRounding < 0)
        {
            var negative = section.AddParagraph("TOTAL NEGATIF — akibat penyesuaian; penanganan pembayaran di luar sistem.");
            negative.Format.Font.Bold = true;
            negative.Format.Font.Color = new Color(180, 30, 30);
            negative.Format.Alignment = ParagraphAlignment.Right;
        }

        // Tanda terima
        section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.8);
        var sign = section.AddTable();
        sign.AddColumn(width - Unit.FromCentimeter(6));
        sign.AddColumn(Unit.FromCentimeter(6));
        var label = sign.AddRow();
        label.Cells[1].AddParagraph("Diterima oleh,").Format.Alignment = ParagraphAlignment.Center;
        var space = sign.AddRow();
        space.Height = Unit.FromCentimeter(1.8);
        space.HeightRule = RowHeightRule.Exactly;
        var name = sign.AddRow();
        var p = name.Cells[1].AddParagraph($"( {w.WorkerName} )");
        p.Format.Alignment = ParagraphAlignment.Center;
    }

    private static void Field(Section section, string label, string value)
    {
        var p = section.AddParagraph();
        p.Format.SpaceAfter = Unit.FromCentimeter(0.08);
        p.AddFormattedText($"{label}: ", TextFormat.NotBold).Color = Muted;
        p.AddFormattedText(value, TextFormat.Bold);
    }

    private static void TotalRow(Table table, string label, string value, bool bold = false, double size = 9)
    {
        var row = table.AddRow();
        var l = row.Cells[0].AddParagraph(label);
        l.Format.Alignment = ParagraphAlignment.Right;
        l.Format.Font.Bold = bold;
        l.Format.Font.Size = size;
        var v = row.Cells[1].AddParagraph(value);
        v.Format.Alignment = ParagraphAlignment.Right;
        v.Format.Font.Bold = bold;
        v.Format.Font.Size = size;
    }

    private static void Cell(Cell cell, string text, ParagraphAlignment alignment, bool bold = false)
    {
        cell.VerticalAlignment = VerticalAlignment.Center;
        var p = cell.AddParagraph(text);
        p.Format.Alignment = alignment;
        p.Format.LeftIndent = alignment == ParagraphAlignment.Left ? Unit.FromCentimeter(0.1) : 0;
        p.Format.RightIndent = alignment == ParagraphAlignment.Right ? Unit.FromCentimeter(0.1) : 0;
        if (bold)
        {
            p.Format.Font.Bold = true;
        }
    }
}
