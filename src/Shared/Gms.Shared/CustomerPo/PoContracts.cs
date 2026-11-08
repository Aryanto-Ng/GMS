using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.CustomerPo;

/// <summary>Nama aksi pada <c>AllowedActions</c> (gabungan status + permission, ditentukan server).</summary>
public static class PoActions
{
    public const string Edit = "edit";
    public const string Confirm = "confirm";
    public const string Cancel = "cancel";
    public const string Allocate = "allocate";
    public const string CreateWorkOrder = "create-wo";
    public const string CreateDelivery = "create-delivery";
    public const string SetCustomerPoNumber = "set-customer-po-number";
}

public sealed record PoListQuery : PageQuery
{
    public PoStatus? Status { get; init; }
    public Guid? CustomerId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    // Filter cepat (juga dipakai Beranda)
    public bool Terlambat { get; init; }
    public bool JatuhTempo7 { get; init; }
    public bool PoCustomerKosong { get; init; }
    public bool SisaRencana { get; init; }
}

public sealed record PoListItem(
    Guid Id,
    string Code,
    string? CustomerPoNumber,
    Guid CustomerId,
    string CustomerName,
    DateOnly PoDate,
    PoStatus Status,
    int TotalQty,
    int DeliveredQty,
    int AllocatedQty,
    int InProductionQty,
    bool HasLateLines);

public sealed record PoLineDto(
    Guid Id,
    Guid FgId,
    string FgCode,
    string FgName,
    ColorSizeRef ColorSize,
    DateOnly DeliveryDate,
    int Qty,
    int DeliveredQty,
    int AllocatedQty,
    int InProductionQty,
    int RemainingPlanQty,
    /// <summary>Batas minimum Qty saat revisi = Terkirim + Dialokasikan + Dalam Produksi.</summary>
    int MinAllowedQty,
    /// <summary>FG/Color/Size terkunci bila baris sudah memiliki alokasi, WO, atau pengiriman.</summary>
    bool IsLocked,
    bool IsLate,
    string? Notes);

public sealed record PoDto(
    Guid Id,
    string Code,
    string? CustomerPoNumber,
    Guid CustomerId,
    string CustomerName,
    DateOnly PoDate,
    PoStatus Status,
    string? Notes,
    string? CancelReason,
    IReadOnlyList<PoLineDto> Lines,
    int TotalQty,
    int DeliveredQty,
    int AllocatedQty,
    int InProductionQty,
    int RemainingPlanQty,
    IReadOnlyList<string> AllowedActions,
    DateTimeOffset CreatedAt,
    string? CreatedByName,
    uint RowVersion);

/// <summary>Baris untuk simpan/revisi. <see cref="Id"/> null = baris baru. Baris yang tidak ikut dikirim akan dihapus (bila diizinkan).</summary>
public sealed record SavePoLine(
    Guid? Id,
    Guid FgId,
    Guid ColorId,
    Guid SizeId,
    DateOnly DeliveryDate,
    int Qty,
    string? Notes);

/// <summary>Buat PO (RowVersion = 0) atau revisi PO. <see cref="ConfirmAfterSave"/> hanya berlaku saat membuat dari Draft.</summary>
public sealed record SavePoRequest(
    Guid CustomerId,
    DateOnly PoDate,
    string? CustomerPoNumber,
    string? Notes,
    IReadOnlyList<SavePoLine> Lines,
    bool ConfirmAfterSave = false,
    uint RowVersion = 0);

public sealed record SetCustomerPoNumberRequest(string? CustomerPoNumber, uint RowVersion);

public sealed record AllocationEntryDto(
    long Id,
    Guid PoLineId,
    string FgCode,
    ColorSizeRef ColorSize,
    AllocationKind Kind,
    int Qty,
    AllocationSource Source,
    string? Reason,
    DateTimeOffset CreatedAt,
    string? CreatedByName);

public sealed record StockAvailabilityDto(
    int OnHand,
    int AllocatedAllPo,
    int Available,
    int RemainingPlanQty,
    int AllocatedToThisLine,
    /// <summary>= min(Tersedia, Sisa Rencana). Batas pengisian dialog alokasi.</summary>
    int MaxAllocatable,
    string? LimitReason);

public sealed record AllocateRequest(int Qty, string? Reason);

public sealed record ReleaseAllocationRequest(int Qty, string? Reason);

public interface IPurchaseOrdersApi
{
    Task<PagedResult<PoListItem>> ListAsync(PoListQuery query, CancellationToken ct = default);
    Task<PoDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<PoDto> CreateAsync(SavePoRequest request, CancellationToken ct = default);
    Task<PoDto> UpdateAsync(Guid id, SavePoRequest request, CancellationToken ct = default);
    Task<PoDto> SetCustomerPoNumberAsync(Guid id, SetCustomerPoNumberRequest request, CancellationToken ct = default);
    Task<PoDto> ConfirmAsync(Guid id, uint rowVersion, CancellationToken ct = default);
    Task<PoDto> CancelAsync(Guid id, ReasonRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AllocationEntryDto>> GetAllocationsAsync(Guid id, CancellationToken ct = default);
    Task<StockAvailabilityDto> GetStockAvailabilityAsync(Guid poLineId, CancellationToken ct = default);
    Task<PoLineDto> AllocateAsync(Guid poLineId, AllocateRequest request, CancellationToken ct = default);
    Task<PoLineDto> ReleaseAllocationAsync(Guid poLineId, ReleaseAllocationRequest request, CancellationToken ct = default);
}
