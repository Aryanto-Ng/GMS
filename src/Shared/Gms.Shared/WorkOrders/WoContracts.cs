using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.WorkOrders;

public static class WoActions
{
    public const string Edit = "edit";
    public const string EditQuantities = "edit-quantities";
    public const string Release = "release";
    public const string Complete = "complete";
    public const string Resume = "resume";
    public const string Close = "close";
    public const string Reopen = "reopen";
    public const string Cancel = "cancel";
    public const string InputOutput = "input-output";
    public const string HandoverFg = "handover-fg";
}

public sealed record WoListQuery : PageQuery
{
    public WoStatus? Status { get; init; }
    public Guid? PoId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? FgId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public bool Terlambat { get; init; }
    public bool MenungguClose { get; init; }
}

public sealed record WoListItem(
    Guid Id,
    string Code,
    Guid PoId,
    string PoCode,
    string CustomerName,
    string FgCode,
    string FgName,
    WoStatus Status,
    int TotalQty,
    /// <summary>Output proses terakhir ÷ Qty WO × 100 (dapat &gt; 100).</summary>
    decimal ProgressPercent,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    bool IsLate);

public sealed record WoLineDto(
    Guid Id,
    Guid PoLineId,
    ColorSizeRef ColorSize,
    int Qty,
    int HandedOverQty,
    int PoRemainingPlanQty);

public sealed record WoDto(
    Guid Id,
    string Code,
    Guid PoId,
    string PoCode,
    string? CustomerPoNumber,
    string CustomerName,
    Guid FgId,
    string FgCode,
    string FgName,
    WoStatus Status,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    string? Notes,
    string? CancelReason,
    IReadOnlyList<WoLineDto> Lines,
    int TotalQty,
    decimal ProgressPercent,
    int HandedOverQty,
    int UnhandedQty,
    int OutstandingAtVendorQty,
    DateTimeOffset? ReleasedAt,
    IReadOnlyList<string> AllowedActions,
    uint RowVersion);

public sealed record WoCreationCell(Guid PoLineId, ColorSizeRef ColorSize, int RemainingPlanQty);

public sealed record WoCreationFgOption(
    Guid FgId,
    string FgCode,
    string FgName,
    bool IsReady,
    IReadOnlyList<ReadinessItem> Readiness,
    IReadOnlyList<WoCreationCell> Cells);

/// <summary>Pilihan FG pada sebuah PO beserta Sisa Rencana per sel (hasil hitung server).</summary>
public sealed record WoCreationOptionsDto(Guid PoId, string PoCode, string CustomerName, IReadOnlyList<WoCreationFgOption> Fgs);

public sealed record SaveWoLine(Guid PoLineId, int Qty);

public sealed record SaveWoRequest(
    Guid PoId,
    Guid FgId,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    string? Notes,
    IReadOnlyList<SaveWoLine> Lines,
    uint RowVersion = 0);

public sealed record UpdateWoQuantitiesRequest(IReadOnlyList<SaveWoLine> Lines, uint RowVersion);

public sealed record UpdateWoScheduleRequest(DateOnly? StartDate, DateOnly? TargetDate, string? Notes, uint RowVersion);

/// <summary>Aksi status dengan alasan opsional/wajib bergantung aksi. <see cref="Reason"/> wajib untuk cancel, resume, reopen.</summary>
public sealed record WoActionRequest(string? Reason, uint RowVersion);

public sealed record WoReleasePreviewDto(
    IReadOnlyList<ReadinessItem> Checks,
    bool CanRelease,
    int RoutingStepCount,
    int TariffCellCount);

public sealed record WoClosePreviewDto(
    int RemainingInProductionQty,
    int ReturnedToPoPlanQty,
    IReadOnlyList<WoUnhandedCell> UnhandedCells);

public sealed record WoUnhandedCell(ColorSizeRef ColorSize, int Qty);

// ===== Matriks progres =====

public sealed record WoProgressCell(Guid WoLineId, int Output, int Target, int Sent);

public sealed record WoStepProgress(
    int Seq,
    string ProcessCode,
    string ProcessName,
    ProcessType ProcessType,
    IReadOnlyList<WoProgressCell> Cells,
    int TotalOutput,
    int TotalTarget,
    decimal Percent,
    StepState State,
    int SentTotal,
    int OutstandingAtVendor);

public sealed record WoProgressDto(
    Guid WoId,
    IReadOnlyList<WoLineDto> Columns,
    IReadOnlyList<WoStepProgress> Steps,
    int HandedOverTotal,
    int UnhandedTotal);

// ===== Snapshot tarif (WorkOrder.Tarif.View) =====

public sealed record WoTariffSnapshotDto(
    DateTimeOffset? ReleasedAt,
    string? ReleasedByName,
    IReadOnlyList<WoSnapshotStep> Steps,
    IReadOnlyList<SizeRef> Sizes,
    IReadOnlyList<WoSnapshotRate> Rates);

public sealed record WoSnapshotStep(int Seq, string ProcessCode, string ProcessName, ProcessType ProcessType);

public sealed record WoSnapshotRate(int Seq, Guid SizeId, decimal Rate);

public sealed record WoStatusHistoryItem(
    WoStatus? FromStatus,
    WoStatus ToStatus,
    string? Reason,
    DateTimeOffset ChangedAt,
    string? ChangedByName);

public interface IWorkOrdersApi
{
    Task<PagedResult<WoListItem>> ListAsync(WoListQuery query, CancellationToken ct = default);
    Task<WoCreationOptionsDto> GetCreationOptionsAsync(Guid poId, CancellationToken ct = default);
    Task<WoDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<WoDto> CreateAsync(SaveWoRequest request, CancellationToken ct = default);
    Task<WoDto> UpdateDraftAsync(Guid id, SaveWoRequest request, CancellationToken ct = default);
    Task<WoDto> UpdateQuantitiesAsync(Guid id, UpdateWoQuantitiesRequest request, CancellationToken ct = default);
    Task<WoDto> UpdateScheduleAsync(Guid id, UpdateWoScheduleRequest request, CancellationToken ct = default);
    Task<WoReleasePreviewDto> PreviewReleaseAsync(Guid id, CancellationToken ct = default);
    Task<WoDto> ReleaseAsync(Guid id, WoActionRequest request, CancellationToken ct = default);
    Task<WoDto> CompleteAsync(Guid id, WoActionRequest request, CancellationToken ct = default);
    Task<WoDto> ResumeAsync(Guid id, WoActionRequest request, CancellationToken ct = default);
    Task<WoClosePreviewDto> PreviewCloseAsync(Guid id, CancellationToken ct = default);
    Task<WoDto> CloseAsync(Guid id, WoActionRequest request, CancellationToken ct = default);
    Task<WoDto> ReopenAsync(Guid id, WoActionRequest request, CancellationToken ct = default);
    Task<WoDto> CancelAsync(Guid id, WoActionRequest request, CancellationToken ct = default);
    Task<WoProgressDto> GetProgressAsync(Guid id, CancellationToken ct = default);
    Task<WoTariffSnapshotDto> GetTariffSnapshotAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<WoStatusHistoryItem>> GetStatusHistoryAsync(Guid id, CancellationToken ct = default);
}
