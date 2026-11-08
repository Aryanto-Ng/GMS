using System.Text;
using Gms.Shared.Problems;

namespace Gms.Api.Common;

public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Cells);

public sealed record CsvTable(IReadOnlyList<string> Header, IReadOnlyList<CsvRow> Rows, char Delimiter);

/// <summary>
/// Pembaca CSV sederhana namun tangguh tanpa package: UTF-8 (dengan/tanpa BOM), pemisah koma atau titik koma
/// (terdeteksi otomatis, FSD AC-MD-30), tanda kutip ganda berformat RFC 4180, CRLF/LF, dan baris kosong diabaikan.
/// Kegagalan tingkat berkas (encoding, ukuran, jumlah baris) dilempar sebagai <c>import-invalid</c>.
/// </summary>
public static class CsvParser
{
    public const int MaxRows = 2000;
    public const long MaxBytes = 2 * 1024 * 1024;

    public static async Task<CsvTable> ParseAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek && stream.Length > MaxBytes)
        {
            throw Invalid("Ukuran berkas melebihi batas 2 MB.");
        }

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        if (bytes.Length > MaxBytes)
        {
            throw Invalid("Ukuran berkas melebihi batas 2 MB.");
        }

        if (bytes.Length == 0)
        {
            throw Invalid("Berkas kosong.");
        }

        string text;
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            text = encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("Berkas harus berencoding UTF-8.");
        }

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var delimiter = DetectDelimiter(text);
        var lines = ReadRecords(text, delimiter);
        if (lines.Count == 0)
        {
            throw Invalid("Berkas tidak berisi data.");
        }

        var header = lines[0].Cells.Select(c => c.Trim()).ToList();
        var rows = lines.Skip(1).Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (rows.Count > MaxRows)
        {
            throw Invalid($"Jumlah baris melebihi batas {MaxRows} baris per impor.");
        }

        return new CsvTable(header, rows, delimiter);
    }

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var commas = 0;
        var semicolons = 0;
        var inQuotes = false;
        foreach (var c in firstLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && c == ',')
            {
                commas++;
            }
            else if (!inQuotes && c == ';')
            {
                semicolons++;
            }
        }

        return semicolons > commas ? ';' : ',';
    }

    private static List<CsvRow> ReadRecords(string text, char delimiter)
    {
        var records = new List<CsvRow>();
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        void EndCell()
        {
            cells.Add(current.ToString());
            current.Clear();
        }

        void EndRecord()
        {
            EndCell();
            records.Add(new CsvRow(recordLine, cells.ToList()));
            cells.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    current.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                EndCell();
            }
            else if (c == '\r')
            {
                // diabaikan; \n mengakhiri record
            }
            else if (c == '\n')
            {
                EndRecord();
                line++;
                recordLine = line;
            }
            else
            {
                current.Append(c);
            }
        }

        if (inQuotes)
        {
            throw Invalid("Tanda kutip pada berkas tidak ditutup.");
        }

        if (current.Length > 0 || cells.Count > 0)
        {
            EndRecord();
        }

        return records;
    }

    public static ProblemException Invalid(string message) =>
        new(StatusCodes.Status422UnprocessableEntity, ProblemTypes.ImportInvalid, "Berkas impor ditolak.", message, ProblemCodes.ImportFileInvalid);
}
