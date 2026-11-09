using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Gms.Api.Documents;

public sealed record PrintLine(string FgCode, string FgName, string ColorCode, string ColorName, int SizeSort, string SizeCode, int Qty);

/// <summary>Data cetak surat jalan yang dikumpulkan handler saat mencetak (AC-PG-23: bersumber dari data saat ini).</summary>
public sealed record DeliveryPrintModel(
    string Code, DateOnly Date, string PoCode, string? CustomerPoNumber, string CustomerName, string? CustomerAddress,
    string? Recipient, string? Vehicle, string? Driver, string? Notes, string ComfiName, string ComfiAddress,
    IReadOnlyList<PrintLine> Lines, bool IsVoided, string? VoidReason, bool IsReprint, string PrintedBy, DateTime PrintedAtLocal);

/// <summary>
/// Pembuat PDF Surat Jalan A4 (AC-PG-17..21): kop, data customer, matriks ukuran (baris FG + Color, kolom Size, Total),
/// tiga kolom tanda tangan, penanda CETAK ULANG, dan watermark DIBATALKAN. Kolom Size dipecah per blok agar tidak melebar (E11).
/// </summary>
public static class DeliveryPdfBuilder
{
    public const int MaxSizeColumnsPerBlock = 8;
    private static readonly Color Ink = Colors.Black;
    private static readonly Color Muted = new(90, 90, 90);
    private static readonly Color HeaderFill = new(235, 235, 235);

    public static byte[] Build(DeliveryPrintModel model, string? fontDirectory)
    {
        PdfFonts.EnsureInitialized(fontDirectory);
        var document = CreateDocument(model);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var pdf = renderer.PdfDocument;
        pdf.Info.Title = $"Surat Jalan {model.Code}";
        pdf.Info.Author = model.ComfiName;
        pdf.Info.Subject = string.Join(" | ", new[] { model.IsVoided ? "DIBATALKAN" : null, model.IsReprint ? "CETAK ULANG" : null }.Where(s => s is not null));

        if (model.IsVoided)
        {
            foreach (var page in pdf.Pages)
            {
                DrawWatermark(page, "DIBATALKAN");
            }
        }

        using var stream = new MemoryStream();
        pdf.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void DrawWatermark(PdfPage page, string text)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var font = new XFont(PdfFonts.Family, 76, XFontStyleEx.Bold);
        var brush = new XSolidBrush(XColor.FromArgb(70, 200, 30, 30));
        gfx.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
        gfx.RotateTransform(-45);
        gfx.DrawString(text, font, brush, new XPoint(0, 0), XStringFormats.Center);
    }

    private static Document CreateDocument(DeliveryPrintModel m)
    {
        var doc = new Document();
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = PdfFonts.Family;
        normal.Font.Size = 9;
        normal.Font.Color = Ink;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        var width = Unit.FromCentimeter(21 - 3.2);

        // Kop Comfi (boleh kosong, E12) + penanda cetak ulang.
        var head = section.AddTable();
        head.AddColumn(width - Unit.FromCentimeter(4));
        head.AddColumn(Unit.FromCentimeter(4));
        var headRow = head.AddRow();
        var company = headRow.Cells[0].AddParagraph(string.IsNullOrWhiteSpace(m.ComfiName) ? " " : m.ComfiName);
        company.Format.Font.Size = 14;
        company.Format.Font.Bold = true;
        if (!string.IsNullOrWhiteSpace(m.ComfiAddress))
        {
            var address = headRow.Cells[0].AddParagraph(m.ComfiAddress);
            address.Format.Font.Size = 8.5;
            address.Format.Font.Color = Muted;
        }

        if (m.IsReprint)
        {
            var mark = headRow.Cells[1].AddParagraph("CETAK ULANG");
            mark.Format.Alignment = ParagraphAlignment.Right;
            mark.Format.Font.Bold = true;
            mark.Format.Font.Size = 10;
        }

        var title = section.AddParagraph("SURAT JALAN");
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.Font.Size = 16;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromCentimeter(0.3);
        title.Format.SpaceAfter = Unit.FromCentimeter(0.3);
        title.Format.Borders.Top.Width = 0.75;
        title.Format.Borders.Bottom.Width = 0.75;

        if (m.IsVoided)
        {
            var voided = section.AddParagraph($"DIBATALKAN{(string.IsNullOrWhiteSpace(m.VoidReason) ? "" : " — " + m.VoidReason)}");
            voided.Format.Alignment = ParagraphAlignment.Center;
            voided.Format.Font.Bold = true;
            voided.Format.Font.Color = new Color(180, 30, 30);
            voided.Format.SpaceAfter = Unit.FromCentimeter(0.2);
        }

        // Data dokumen (kiri) dan penerima (kanan).
        var info = section.AddTable();
        info.AddColumn(width / 2);
        info.AddColumn(width / 2);
        var infoRow = info.AddRow();
        Field(infoRow.Cells[0], "Kode SJ", m.Code);
        Field(infoRow.Cells[0], "Tanggal Kirim", m.Date.ToString("dd-MM-yyyy"));
        Field(infoRow.Cells[0], "PO Internal", m.PoCode);
        Field(infoRow.Cells[0], "No. PO Customer", string.IsNullOrWhiteSpace(m.CustomerPoNumber) ? "-" : m.CustomerPoNumber);
        Field(infoRow.Cells[1], "Customer", m.CustomerName);
        Field(infoRow.Cells[1], "Alamat", string.IsNullOrWhiteSpace(m.CustomerAddress) ? "-" : m.CustomerAddress);
        Field(infoRow.Cells[1], "Penerima", m.Recipient ?? "-");
        Field(infoRow.Cells[1], "Kendaraan / Sopir", $"{m.Vehicle ?? "-"} / {m.Driver ?? "-"}");

        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            var notes = section.AddParagraph($"Catatan: {m.Notes}");
            notes.Format.SpaceBefore = Unit.FromCentimeter(0.2);
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);
        AddMatrix(section, m, width);

        var total = section.AddParagraph($"Total Keseluruhan: {m.Lines.Sum(l => l.Qty):N0} pcs");
        total.Format.Font.Bold = true;
        total.Format.Alignment = ParagraphAlignment.Right;
        total.Format.SpaceBefore = Unit.FromCentimeter(0.3);

        AddSignatures(section, width);

        var footer = section.Footers.Primary.AddParagraph($"Dicetak oleh {m.PrintedBy} pada {m.PrintedAtLocal:dd-MM-yyyy HH:mm} WIB");
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        footer.Format.Alignment = ParagraphAlignment.Right;
        return doc;
    }

    private static void Field(Cell cell, string label, string value)
    {
        var p = cell.AddParagraph();
        p.Format.SpaceAfter = Unit.FromCentimeter(0.08);
        var l = p.AddFormattedText($"{label}: ", TextFormat.NotBold);
        l.Color = Muted;
        p.AddFormattedText(value, TextFormat.Bold);
    }

    private static void AddMatrix(Section section, DeliveryPrintModel m, Unit width)
    {
        var sizes = m.Lines.Select(l => (l.SizeSort, l.SizeCode)).Distinct().OrderBy(s => s.SizeSort).ThenBy(s => s.SizeCode).ToList();
        var rows = m.Lines.GroupBy(l => (l.FgCode, l.FgName, l.ColorCode, l.ColorName))
            .OrderBy(g => g.Key.FgCode).ThenBy(g => g.Key.ColorCode).ToList();

        var blocks = sizes.Chunk(MaxSizeColumnsPerBlock).ToList();
        for (var b = 0; b < blocks.Count; b++)
        {
            var block = blocks[b];
            var table = section.AddTable();
            table.Borders.Width = 0.5;
            table.Borders.Color = Muted;
            var sizeWidth = Unit.FromCentimeter(1.4);
            var totalWidth = Unit.FromCentimeter(1.6);
            table.AddColumn(width - sizeWidth * block.Length - totalWidth);
            foreach (var _ in block)
            {
                table.AddColumn(sizeWidth);
            }

            table.AddColumn(totalWidth);

            var header = table.AddRow();
            header.HeadingFormat = true; // header kolom Size diulang bila tabel pindah halaman
            header.Shading.Color = HeaderFill;
            header.Format.Font.Bold = true;
            Cell(header.Cells[0], b == 0 ? "Barang (FG / Color)" : "Barang (FG / Color) — lanjutan Size", ParagraphAlignment.Left);
            for (var i = 0; i < block.Length; i++)
            {
                Cell(header.Cells[i + 1], block[i].SizeCode, ParagraphAlignment.Center);
            }

            Cell(header.Cells[block.Length + 1], "Total", ParagraphAlignment.Right);

            foreach (var group in rows)
            {
                var row = table.AddRow();
                Cell(row.Cells[0], $"{group.Key.FgCode} — {group.Key.FgName}\n{group.Key.ColorCode} {group.Key.ColorName}", ParagraphAlignment.Left);
                var rowTotal = 0;
                for (var i = 0; i < block.Length; i++)
                {
                    var qty = group.Where(l => l.SizeSort == block[i].SizeSort && l.SizeCode == block[i].SizeCode).Sum(l => l.Qty);
                    rowTotal += qty;
                    Cell(row.Cells[i + 1], qty == 0 ? "-" : qty.ToString("N0"), ParagraphAlignment.Center);
                }

                Cell(row.Cells[block.Length + 1], rowTotal.ToString("N0"), ParagraphAlignment.Right, bold: true);
            }

            var totals = table.AddRow();
            totals.Shading.Color = HeaderFill;
            Cell(totals.Cells[0], blocks.Count > 1 ? $"Total blok {b + 1}" : "Total", ParagraphAlignment.Left, bold: true);
            var blockTotal = 0;
            for (var i = 0; i < block.Length; i++)
            {
                var qty = m.Lines.Where(l => l.SizeSort == block[i].SizeSort && l.SizeCode == block[i].SizeCode).Sum(l => l.Qty);
                blockTotal += qty;
                Cell(totals.Cells[i + 1], qty.ToString("N0"), ParagraphAlignment.Center, bold: true);
            }

            Cell(totals.Cells[block.Length + 1], blockTotal.ToString("N0"), ParagraphAlignment.Right, bold: true);
            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.2);
        }
    }

    private static void Cell(Cell cell, string text, ParagraphAlignment alignment, bool bold = false)
    {
        cell.VerticalAlignment = VerticalAlignment.Center;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var p = cell.AddParagraph(lines[i]);
            p.Format.Alignment = alignment;
            p.Format.LeftIndent = alignment == ParagraphAlignment.Left ? Unit.FromCentimeter(0.1) : 0;
            p.Format.RightIndent = alignment == ParagraphAlignment.Right ? Unit.FromCentimeter(0.1) : 0;
            if (bold)
            {
                p.Format.Font.Bold = true;
            }

            if (i > 0)
            {
                p.Format.Font.Size = 8;
                p.Format.Font.Color = Muted;
            }
        }
    }

    private static void AddSignatures(Section section, Unit width)
    {
        section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(0.6);
        var table = section.AddTable();
        for (var i = 0; i < 3; i++)
        {
            table.AddColumn(width / 3);
        }

        var labels = table.AddRow();
        foreach (var (cell, label) in labels.Cells.Cast<Cell>().Zip(new[] { "Pengirim", "Sopir", "Penerima" }))
        {
            var p = cell.AddParagraph(label);
            p.Format.Alignment = ParagraphAlignment.Center;
            p.Format.Font.Bold = true;
        }

        var space = table.AddRow();
        space.Height = Unit.FromCentimeter(2.0);
        space.HeightRule = RowHeightRule.Exactly;
        var line = table.AddRow();
        foreach (var cell in line.Cells.Cast<Cell>())
        {
            var p = cell.AddParagraph("(............................)");
            p.Format.Alignment = ParagraphAlignment.Center;
        }
    }
}
