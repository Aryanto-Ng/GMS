using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.Production;

// ===== Input Output =====

/// <summary>Satu kolom grid input (Color × Size milik WO) beserta batas yang tersedia (hasil hitung server).</summary>
public sealed record InputColumn(
    Guid WoLineId,
    ColorSizeRef ColorSize,
    /// <summary>Batas maksimum yang boleh diinput. Null = tanpa batas (langkah pertama).</summary>
    int? MaxAllowed,
    int WoQty,
    int CurrentCumulative);

public sealed record InputContextDto(
    Guid WoId,
    string WoCode,
    int Seq,
    string ProcessName,
    DateOnly WorkDate,
    int PreviousStepTotal,
    int CurrentTotal,
    int AvailableTotal,
    bool IsFirstStep,
    IReadOnlyList<InputColumn> Columns,
    IReadOnlyList<string> Warnings,
    /// <summary>Terisi bila input tidak diizinkan (status WO, tanggal di periode tertutup, dst.).</summary>
    string? BlockedReason);

public sealed record OutputRow(Guid WorkerId, IReadOnlyList<WoLineQty> Cells);

/// <summary>Satu penyimpanan memuat banyak pekerja; semua-atau-tidak-sama-sekali. Satu entri dibuat per pekerja.</summary>
public sealed record SaveOutputsRequest(
    Guid WoId,
    int Seq,
    DateOnly WorkDate,
    IReadOnlyList<OutputRow> Rows,
    bool ConfirmExceedWoQty = false);

public sealed record SaveOutputsResult(IReadOnlyList<Guid> EntryIds, int TotalQty, int WorkerCount, IReadOnlyList<string> Warnings);

// ===== Riwayat gabungan =====

public sealed record EntryListQuery : PageQuery
{
    public Guid? WoId { get; init; }
    public int? Seq { get; init; }
    public Guid? WorkerId { get; init; }
    public Guid? VendorId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public EntryKind? Kind { get; init; }
    public DocStatus? Status { get; init; }
    public bool HariIni { get; init; }

    /// <summary>False = hanya yang aktif (default). True = termasuk Voided.</summary>
    public bool IncludeVoided { get; init; }
}

public sealed record EntryListItem(
    Guid Id,
    EntryKind Kind,
    Guid WoId,
    string WoCode,
    int Seq,
    string ProcessName,
    Guid? WorkerId,
    string? WorkerName,
    Guid? VendorId,
    string? VendorName,
    DateOnly WorkDate,
    int TotalQty,
    DocStatus Status,
    DateTimeOffset CreatedAt,
    string? CreatedByName,
    string? VoidReason,
    Guid? ReplacesId,
    Guid? ReplacedById,
    bool CanVoid,
    bool CanCorrect,
    /// <summary>Alasan aksi dinonaktifkan (WO Completed/Closed, periode gaji tertutup, dst.).</summary>
    string? DisabledReason);

public sealed record EntryLineDto(Guid WoLineId, ColorSizeRef ColorSize, int Qty);

public sealed record EntryDetailDto(
    EntryListItem Item,
    IReadOnlyList<EntryLineDto> Lines,
    string? Reason,
    DateOnly? OriginalDate,
    Guid? RefEntryId,
    string? DocNo,
    string? Notes);

public sealed record CorrectOutputRequest(string Reason, Guid WorkerId, DateOnly WorkDate, IReadOnlyList<WoLineQty> Cells);

/// <summary>Penyesuaian untuk periode gaji tertutup. Qty boleh negatif. OriginalDate harus berada pada periode tertutup.</summary>
public sealed record AdjustmentRequest(
    Guid WoId,
    int Seq,
    Guid WorkerId,
    DateOnly OriginalDate,
    DateOnly WorkDate,
    string Reason,
    IReadOnlyList<WoLineQty> Cells);

// ===== Vendor =====

public sealed record VendorColumn(
    Guid WoLineId,
    ColorSizeRef ColorSize,
    /// <summary>Batas kirim = Output langkah sebelumnya − Terkirim sebelumnya (atau Qty WO − Terkirim bila langkah vendor pertama).</summary>
    int MaxSend,
    /// <summary>Batas terima untuk vendor terpilih = Terkirim − Diterima (per vendor).</summary>
    int MaxReceive,
    int SentTotal,
    int ReceivedTotal);

public sealed record VendorSummary(Guid VendorId, string VendorName, int Sent, int Received, int Outstanding);

public sealed record VendorContextDto(
    Guid WoId,
    string WoCode,
    int Seq,
    string ProcessName,
    Guid? VendorId,
    IReadOnlyList<VendorColumn> Columns,
    IReadOnlyList<VendorSummary> Vendors,
    string? BlockedReason);

public sealed record SaveVendorMovementRequest(
    Guid WoId,
    int Seq,
    Guid VendorId,
    VendorMovementKind Kind,
    DateOnly Date,
    string? DocNo,
    string? Notes,
    IReadOnlyList<WoLineQty> Cells);

public sealed record CorrectVendorMovementRequest(
    string Reason,
    Guid VendorId,
    DateOnly Date,
    string? DocNo,
    string? Notes,
    IReadOnlyList<WoLineQty> Cells);

public interface IProductionApi
{
    // Output pekerja
    Task<InputContextDto> GetInputContextAsync(Guid woId, int seq, DateOnly workDate, CancellationToken ct = default);
    Task<SaveOutputsResult> SaveOutputsAsync(SaveOutputsRequest request, CancellationToken ct = default);
    Task<PagedResult<EntryListItem>> ListEntriesAsync(EntryListQuery query, CancellationToken ct = default);
    Task<EntryDetailDto> GetEntryAsync(Guid id, CancellationToken ct = default);
    Task<ImpactPreview> PreviewVoidAsync(Guid id, CancellationToken ct = default);
    Task VoidAsync(Guid id, ReasonRequest request, CancellationToken ct = default);
    Task<ImpactPreview> PreviewCorrectOutputAsync(Guid id, CorrectOutputRequest request, CancellationToken ct = default);
    Task<Guid> CorrectOutputAsync(Guid id, CorrectOutputRequest request, CancellationToken ct = default);
    Task<Guid> CreateAdjustmentAsync(AdjustmentRequest request, CancellationToken ct = default);

    // Vendor
    Task<VendorContextDto> GetVendorContextAsync(Guid woId, int seq, Guid? vendorId, CancellationToken ct = default);
    Task<Guid> SaveVendorMovementAsync(SaveVendorMovementRequest request, CancellationToken ct = default);
    Task<ImpactPreview> PreviewVoidVendorMovementAsync(Guid id, CancellationToken ct = default);
    Task VoidVendorMovementAsync(Guid id, ReasonRequest request, CancellationToken ct = default);
    Task<ImpactPreview> PreviewCorrectVendorMovementAsync(Guid id, CorrectVendorMovementRequest request, CancellationToken ct = default);
    Task<Guid> CorrectVendorMovementAsync(Guid id, CorrectVendorMovementRequest request, CancellationToken ct = default);
}
