using Gms.Web.Infrastructure.Format;

namespace Gms.Web.Components.Shared;

public sealed record MatrixRow(string Key, string Label, string? SubLabel = null);

public sealed record MatrixColumn(string Key, string Label);

/// <summary>Aturan satu sel: <see cref="Enabled"/> false = tidak berlaku ("—"); <see cref="Max"/> = batas keras; <see cref="SoftMax"/> = batas lunak (peringatan, tidak memblokir).</summary>
public sealed record MatrixCellInfo(bool Enabled = true, int? Max = null, int? SoftMax = null);

public sealed record MatrixIssue(string RowKey, string ColumnKey, string Message, bool IsError);

/// <summary>Nilai grid (baris × kolom → qty). Sel kosong = tidak ada nilai.</summary>
public sealed class MatrixValues
{
    private readonly Dictionary<(string Row, string Col), int> _values = [];

    public int? Get(string row, string column) => _values.TryGetValue((row, column), out var v) ? v : null;

    public void Set(string row, string column, int? value)
    {
        if (value is null)
        {
            _values.Remove((row, column));
        }
        else
        {
            _values[(row, column)] = value.Value;
        }
    }

    public IEnumerable<(string Row, string Column, int Qty)> All => _values.Select(kv => (kv.Key.Row, kv.Key.Col, kv.Value));

    public static MatrixValues From(IEnumerable<(string Row, string Column, int Qty)> cells)
    {
        var values = new MatrixValues();
        foreach (var (row, column, qty) in cells)
        {
            values.Set(row, column, qty);
        }

        return values;
    }
}

/// <summary>Logika murni grid matriks ukuran (total, isu batas, tempel blok) agar dapat diuji tanpa render.</summary>
public static class SizeMatrixLogic
{
    public static int RowTotal(MatrixValues values, string row, IEnumerable<MatrixColumn> columns, Func<string, string, MatrixCellInfo> info) =>
        columns.Where(c => info(row, c.Key).Enabled).Sum(c => values.Get(row, c.Key) ?? 0);

    public static int ColumnTotal(MatrixValues values, string column, IEnumerable<MatrixRow> rows, Func<string, string, MatrixCellInfo> info) =>
        rows.Where(r => info(r.Key, column).Enabled).Sum(r => values.Get(r.Key, column) ?? 0);

    public static IReadOnlyList<MatrixIssue> Evaluate(
        IReadOnlyList<MatrixRow> rows, IReadOnlyList<MatrixColumn> columns, MatrixValues values, Func<string, string, MatrixCellInfo> info)
    {
        var issues = new List<MatrixIssue>();
        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                var cell = info(row.Key, column.Key);
                if (!cell.Enabled || values.Get(row.Key, column.Key) is not { } qty)
                {
                    continue;
                }

                if (cell.Max is { } max && qty > max)
                {
                    issues.Add(new MatrixIssue(row.Key, column.Key, $"{row.Label}, Size {column.Label}: melebihi batas {IdFormat.Qty(max)}.", IsError: true));
                }
                else if (cell.SoftMax is { } soft && qty > soft)
                {
                    issues.Add(new MatrixIssue(row.Key, column.Key, $"{row.Label}, Size {column.Label}: melebihi Qty WO ({IdFormat.Qty(soft)}); perlu konfirmasi.", IsError: false));
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// Menempel blok dari Excel (baris dipisah newline, kolom dipisah tab) mulai dari sel (startRow, startCol). Hanya sel berlaku yang terisi;
    /// teks bukan bilangan bulat dilewati dan dilaporkan (tidak ditebak). Sel kosong pada blok dibiarkan.
    /// </summary>
    public static (int Applied, int Skipped) Paste(
        IReadOnlyList<MatrixRow> rows, IReadOnlyList<MatrixColumn> columns, MatrixValues values, Func<string, string, MatrixCellInfo> info,
        int startRow, int startCol, string text, int maxValue = 9_999_999)
    {
        var applied = 0;
        var skipped = 0;
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1]; // Excel menambah newline di akhir
        }

        for (var r = 0; r < lines.Length; r++)
        {
            var cells = lines[r].Split('\t');
            for (var c = 0; c < cells.Length; c++)
            {
                var ri = startRow + r;
                var ci = startCol + c;
                var raw = cells[c].Trim();
                if (raw.Length == 0)
                {
                    continue;
                }

                if (ri >= rows.Count || ci >= columns.Count || !info(rows[ri].Key, columns[ci].Key).Enabled)
                {
                    skipped++;
                    continue;
                }

                if (IdNumber.TryParseInt(raw, out var qty, out _) && qty >= 0 && qty <= maxValue)
                {
                    values.Set(rows[ri].Key, columns[ci].Key, qty);
                    applied++;
                }
                else
                {
                    skipped++;
                }
            }
        }

        return (applied, skipped);
    }
}
