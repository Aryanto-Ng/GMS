using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.Deliveries;

public static class DeliveryActions
{
    public const string EditInfo = "edit-info";
    public const string Void = "void";
    public const string Correct = "correct";
    public const string Print = "print";
}

/// <summary>PO yang masih memiliki kebutuhan tersisa untuk dikirim (pemilih PO pada form surat jalan).</summary>
public sealed record EligiblePoItem(Guid PoId, string Code, string? CustomerPoNumber, string CustomerName, int OpenQty, int AllocatedQty);

public sealed record DeliveryLineOption(
    Guid PoLineId,
    string FgCode,
    string FgName,
    ColorSizeRef ColorSize,
    DateOnly PoDeliveryDate,
    int Qty,
    int DeliveredQty,
    int AllocatedQty,
    /// <summary>= min(Dialokasikan, Qty − Terkirim). Hasil hitung server.</summary>
    int MaxDeliver,
    int StockAvailable,
    bool IsLate);

public sealed record DeliveryContextDto(
    Guid PoId,
    string PoCode,
    string? CustomerPoNumber,
    string CustomerName,
    PoStatus PoStatus,
    IReadOnlyList<DeliveryLineOption> Lines,
    IReadOnlyList<string> Warnings);

public sealed record DeliveryLineQty(Guid PoLineId, int Qty);

public sealed record SaveDeliveryRequest(
    Guid PoId,
    DateOnly DeliveryDate,
    string? RecipientName,
    string? Vehicle,
    string? Driver,
    string? Notes,
    IReadOnlyList<DeliveryLineQty> Lines);

public sealed record DeliveryListQuery : PageQuery
{
    public Guid? PoId { get; init; }
    public Guid? CustomerId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public DocStatus? Status { get; init; }
}

public sealed record DeliveryListItem(
    Guid Id,
    string Code,
    DateOnly DeliveryDate,
    Guid PoId,
    string PoCode,
    string? CustomerPoNumber,
    string CustomerName,
    int TotalQty,
    DocStatus Status,
    int PrintCount,
    string? CreatedByName);

public sealed record DeliveryLineDto(Guid PoLineId, string FgCode, string FgName, ColorSizeRef ColorSize, int Qty);

public sealed record DeliveryDto(
    DeliveryListItem Item,
    string? RecipientName,
    string? Vehicle,
    string? Driver,
    string? Notes,
    IReadOnlyList<DeliveryLineDto> Lines,
    string? VoidReason,
    Guid? ReplacesId,
    Guid? ReplacedById,
    IReadOnlyList<string> AllowedActions,
    uint RowVersion);

/// <summary>Hanya informasi non-qty yang boleh diubah setelah disimpan.</summary>
public sealed record UpdateDeliveryInfoRequest(
    DateOnly DeliveryDate,
    string? RecipientName,
    string? Vehicle,
    string? Driver,
    string? Notes,
    uint RowVersion);

public sealed record CorrectDeliveryRequest(
    string Reason,
    DateOnly DeliveryDate,
    string? RecipientName,
    string? Vehicle,
    string? Driver,
    string? Notes,
    IReadOnlyList<DeliveryLineQty> Lines);

public interface IDeliveriesApi
{
    Task<IReadOnlyList<EligiblePoItem>> ListEligiblePosAsync(string? q, CancellationToken ct = default);
    Task<DeliveryContextDto> GetContextAsync(Guid poId, CancellationToken ct = default);
    Task<PagedResult<DeliveryListItem>> ListAsync(DeliveryListQuery query, CancellationToken ct = default);
    Task<DeliveryDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<DeliveryDto> CreateAsync(SaveDeliveryRequest request, CancellationToken ct = default);
    Task<DeliveryDto> UpdateInfoAsync(Guid id, UpdateDeliveryInfoRequest request, CancellationToken ct = default);
    Task<ImpactPreview> PreviewVoidAsync(Guid id, CancellationToken ct = default);
    Task<DeliveryDto> VoidAsync(Guid id, ReasonRequest request, CancellationToken ct = default);
    Task<ImpactPreview> PreviewCorrectAsync(Guid id, CorrectDeliveryRequest request, CancellationToken ct = default);
    Task<DeliveryDto> CorrectAsync(Guid id, CorrectDeliveryRequest request, CancellationToken ct = default);

    /// <summary>Membuat PDF surat jalan; menaikkan print_count dan mencatat audit.</summary>
    Task<FileContent> GetPdfAsync(Guid id, CancellationToken ct = default);
}
