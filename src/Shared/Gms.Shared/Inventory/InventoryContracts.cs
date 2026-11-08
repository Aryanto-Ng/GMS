using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.Inventory;

// ===== Serah Terima FG (Konveksi → Comfi) =====

public sealed record HandoverColumn(
    Guid WoLineId,
    ColorSizeRef ColorSize,
    /// <summary>Output kumulatif langkah terakhir − sudah diserahterimakan (hasil hitung server).</summary>
    int MaxHandover,
    int HandedOver,
    int LastStepOutput,
    int WoQty);

public sealed record HandoverFgContextDto(
    Guid WoId,
    string WoCode,
    string PoCode,
    string FgCode,
    string FgName,
    IReadOnlyList<HandoverColumn> Columns,
    string? BlockedReason);

public sealed record SaveHandoverFgRequest(
    Guid WoId,
    DateOnly Date,
    string? DocNo,
    string? ReceivedBy,
    string? Notes,
    IReadOnlyList<WoLineQty> Cells);

public sealed record HandoverCellEstimate(Guid WoLineId, int AutoAllocatedQty, int FreeStockQty);

/// <summary>Perkiraan pembagian qty ke PO (alokasi otomatis) vs stok bebas sebelum simpan.</summary>
public sealed record HandoverFgPreviewDto(
    int AutoAllocatedQty,
    int FreeStockQty,
    string PoCode,
    IReadOnlyList<HandoverCellEstimate> Cells);

public sealed record HandoverFgResultDto(Guid Id, int AutoAllocatedQty, int FreeStockQty, string PoCode);

public sealed record HandoverFgListQuery : PageQuery
{
    public Guid? WoId { get; init; }
    public Guid? PoId { get; init; }
    public Guid? FgId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public DocStatus? Status { get; init; }
}

public sealed record HandoverFgListItem(
    Guid Id,
    DateOnly Date,
    string? DocNo,
    Guid WoId,
    string WoCode,
    string PoCode,
    string FgCode,
    int TotalQty,
    DocStatus Status,
    string? CreatedByName);

public sealed record HandoverFgLineDto(Guid WoLineId, ColorSizeRef ColorSize, int Qty, int AutoAllocatedQty);

public sealed record HandoverFgDto(
    HandoverFgListItem Item,
    IReadOnlyList<HandoverFgLineDto> Lines,
    string? ReceivedBy,
    string? Notes,
    string? VoidReason,
    Guid? ReplacesId,
    Guid? ReplacedById,
    bool CanVoid,
    bool CanCorrect,
    string? DisabledReason);

public sealed record CorrectHandoverFgRequest(
    string Reason,
    DateOnly Date,
    string? DocNo,
    string? ReceivedBy,
    string? Notes,
    IReadOnlyList<WoLineQty> Cells);

// ===== Serah Terima Material (catatan sederhana, tanpa stok) =====

public sealed record MaterialHandoverLine(Guid MaterialId, decimal Qty, string? Notes);

/// <summary>Qty desimal maksimal 3 digit di belakang koma.</summary>
public sealed record SaveMaterialHandoverRequest(
    Guid WoId,
    DateOnly Date,
    string? DocNo,
    string? Notes,
    IReadOnlyList<MaterialHandoverLine> Lines);

public sealed record MaterialHandoverListQuery : PageQuery
{
    public Guid? WoId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public DocStatus? Status { get; init; }
}

public sealed record MaterialHandoverListItem(
    Guid Id,
    DateOnly Date,
    string? DocNo,
    Guid WoId,
    string WoCode,
    int ItemCount,
    DocStatus Status,
    string? CreatedByName);

public sealed record MaterialHandoverLineDto(
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string UomCode,
    decimal Qty,
    string? Notes);

public sealed record MaterialHandoverDto(
    MaterialHandoverListItem Item,
    IReadOnlyList<MaterialHandoverLineDto> Lines,
    string? Notes,
    string? VoidReason,
    bool CanVoid);

// ===== Stok FG =====

public sealed record StockQuery : PageQuery
{
    public Guid? FgId { get; init; }
    public Guid? ColorId { get; init; }
    public Guid? SizeId { get; init; }
    public Guid? CustomerId { get; init; }
    public bool HanyaAdaStok { get; init; } = true;
}

public sealed record StockRow(
    Guid FgId,
    string FgCode,
    string FgName,
    ColorSizeRef ColorSize,
    int OnHand,
    int Allocated,
    int Available);

public sealed record StockListResult(
    PagedResult<StockRow> Rows,
    int OnHandTotal,
    int AllocatedTotal,
    int AvailableTotal);

public sealed record StockLedgerQuery : PageQuery
{
    public Guid FgId { get; init; }
    public Guid ColorId { get; init; }
    public Guid SizeId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record StockLedgerRow(
    long Id,
    DateOnly EntryDate,
    StockLedgerKind Kind,
    string? SourceType,
    Guid? SourceId,
    string? SourceLabel,
    int Qty,
    int RunningBalance,
    string? Reason,
    string? CreatedByName);

public sealed record StockLedgerResult(StockRow Header, PagedResult<StockLedgerRow> Rows);

public sealed record AdjustStockRequest(Guid FgId, Guid ColorId, Guid SizeId, int Qty, DateOnly Date, string Reason);

public interface IInventoryApi
{
    // Serah Terima FG
    Task<HandoverFgContextDto> GetHandoverFgContextAsync(Guid woId, CancellationToken ct = default);
    Task<HandoverFgPreviewDto> PreviewHandoverFgAsync(SaveHandoverFgRequest request, CancellationToken ct = default);
    Task<HandoverFgResultDto> CreateHandoverFgAsync(SaveHandoverFgRequest request, CancellationToken ct = default);
    Task<PagedResult<HandoverFgListItem>> ListHandoverFgAsync(HandoverFgListQuery query, CancellationToken ct = default);
    Task<HandoverFgDto> GetHandoverFgAsync(Guid id, CancellationToken ct = default);
    Task<ImpactPreview> PreviewVoidHandoverFgAsync(Guid id, CancellationToken ct = default);
    Task VoidHandoverFgAsync(Guid id, ReasonRequest request, CancellationToken ct = default);
    Task<ImpactPreview> PreviewCorrectHandoverFgAsync(Guid id, CorrectHandoverFgRequest request, CancellationToken ct = default);
    Task<HandoverFgResultDto> CorrectHandoverFgAsync(Guid id, CorrectHandoverFgRequest request, CancellationToken ct = default);

    // Serah Terima Material
    Task<Guid> CreateMaterialHandoverAsync(SaveMaterialHandoverRequest request, CancellationToken ct = default);
    Task<PagedResult<MaterialHandoverListItem>> ListMaterialHandoversAsync(MaterialHandoverListQuery query, CancellationToken ct = default);
    Task<MaterialHandoverDto> GetMaterialHandoverAsync(Guid id, CancellationToken ct = default);
    Task VoidMaterialHandoverAsync(Guid id, ReasonRequest request, CancellationToken ct = default);

    // Stok FG
    Task<StockListResult> ListStockAsync(StockQuery query, CancellationToken ct = default);
    Task<StockLedgerResult> GetStockLedgerAsync(StockLedgerQuery query, CancellationToken ct = default);
    Task<ImpactPreview> PreviewAdjustStockAsync(AdjustStockRequest request, CancellationToken ct = default);
    Task AdjustStockAsync(AdjustStockRequest request, CancellationToken ct = default);
}
