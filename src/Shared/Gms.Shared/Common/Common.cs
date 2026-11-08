namespace Gms.Shared.Common;

/// <summary>Hasil daftar berhalaman (ApiConventions.md bagian 1).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Parameter dasar daftar. Query spesifik modul mewarisi record ini.</summary>
public record PageQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    /// <summary>Pencarian teks bebas.</summary>
    public string? Q { get; init; }

    /// <summary>Format <c>field:asc</c> atau <c>field:desc</c>.</summary>
    public string? Sort { get; init; }
}

/// <summary>Item ringan untuk dropdown/combobox (hanya data aktif).</summary>
public sealed record LookupItem(Guid Id, string Code, string Name);

/// <summary>Rujukan Size (urut menurut <see cref="SortOrder"/>).</summary>
public sealed record SizeRef(Guid Id, string Code, string Name, int SortOrder);

/// <summary>Rujukan kombinasi Color × Size. Dipakai oleh semua grid matriks (label disertakan agar klien tidak join).</summary>
public sealed record ColorSizeRef(
    Guid ColorId,
    string ColorCode,
    string ColorName,
    Guid SizeId,
    string SizeCode,
    string SizeName,
    int SizeSortOrder);

/// <summary>Satu sel kuantitas pada grid, dikunci oleh baris WO.</summary>
public sealed record WoLineQty(Guid WoLineId, int Qty);

/// <summary>Penahan aturan bisnis (sel, kuantitas saat ini, batas, pesan).</summary>
public sealed record RuleViolation(string Code, string Subject, decimal? Current, decimal? Limit, string Message);

/// <summary>Satu baris tabel dampak pada dialog Void/Koreksi/Rilis (sebelum → sesudah).</summary>
public sealed record ImpactRow(string Subject, string Metric, decimal? Before, decimal? After, decimal? Limit, bool Ok, string? Message);

/// <summary>Pratinjau dampak sebuah aksi tanpa menyimpan apa pun. Hasil hitung server.</summary>
public sealed record ImpactPreview(
    IReadOnlyList<ImpactRow> Rows,
    bool CanProceed,
    IReadOnlyList<RuleViolation> Violations,
    IReadOnlyList<string> Notes);

/// <summary>Butir daftar periksa (kesiapan FG, syarat rilis WO).</summary>
public sealed record ReadinessItem(string Code, bool Ok, string Message, string? FixTarget);

/// <summary>Permintaan aksi sederhana dengan alasan wajib (void, cancel, reopen, dst.).</summary>
public sealed record ReasonRequest(string Reason, uint RowVersion = 0);

/// <summary>Berkas unduhan (PDF, xlsx, csv).</summary>
public sealed record FileContent(string FileName, string ContentType, byte[] Bytes);
