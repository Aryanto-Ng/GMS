using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.Payroll;

// ===== Periode =====

public sealed record PayrollPeriodListItem(
    Guid Id,
    string Code,
    DateOnly StartDate,
    DateOnly EndDate,
    PayrollStatus Status,
    int WorkerCount,
    /// <summary>Open = total sementara (dihitung langsung). Closed = total beku dari snapshot.</summary>
    decimal TotalPaid,
    bool IsProvisional,
    DateTimeOffset? ClosedAt,
    string? ClosedByName);

public sealed record PayrollPeriodDto(
    Guid Id,
    string Code,
    DateOnly StartDate,
    DateOnly EndDate,
    PayrollStatus Status,
    string? Notes,
    int WorkerCount,
    decimal TotalPaid,
    bool IsProvisional,
    DateTimeOffset? ClosedAt,
    string? ClosedByName,
    bool CanClose,
    bool CanEdit,
    uint RowVersion);

/// <summary>Petunjuk pembuatan periode berikutnya (tanggal mulai terkunci kecuali periode pertama).</summary>
public sealed record NextPeriodDefaultsDto(
    DateOnly? StartDate,
    bool StartLocked,
    DateOnly? OldestOutputDate,
    bool HasOpenPeriod);

/// <summary><see cref="StartDate"/> hanya dipakai untuk periode pertama (atau mengubah awal periode pertama yang masih Open).</summary>
public sealed record SavePeriodRequest(DateOnly? StartDate, DateOnly EndDate, string? Notes, uint RowVersion = 0);

/// <summary>Konfirmasi tutup periode: pengguna mengetik kode periode.</summary>
public sealed record ClosePeriodRequest(string ConfirmationCode, uint RowVersion);

public sealed record PayrollNegativeWorker(Guid WorkerId, string WorkerCode, string WorkerName, decimal Amount);

public sealed record ClosePreviewDto(
    int WorkerCount,
    decimal TotalPaid,
    IReadOnlyList<PayrollNegativeWorker> NegativeWorkers,
    bool EndDateInFuture,
    string ConfirmationCode);

// ===== Rekap =====

public sealed record PayrollSummaryRow(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    int TotalQty,
    decimal SumBeforeRounding,
    decimal Paid,
    bool IsNegative);

public sealed record PayrollSummaryDto(
    IReadOnlyList<PayrollSummaryRow> Rows,
    decimal TotalPaid,
    int WorkerCount,
    bool IsProvisional);

/// <summary>Baris rinci harian. Tarif dan subtotal hanya untuk pengguna dengan Payroll.View.</summary>
public sealed record PayrollDailyRow(
    DateOnly WorkDate,
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    Guid WoId,
    string WoCode,
    string ProcessName,
    string SizeCode,
    OutputKind Kind,
    int Qty,
    decimal Rate,
    decimal Subtotal,
    DateOnly? OriginalDate,
    string? Reason,
    Guid SourceEntryId);

public sealed record PayrollDailyQuery : PageQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? WorkerId { get; init; }
    public Guid? WoId { get; init; }
    public string? ProcessCode { get; init; }
}

public sealed record PayrollDailyResult(PagedResult<PayrollDailyRow> Rows, decimal Subtotal);

/// <summary>Kelompok WO → Proses → Size pada rincian satu pekerja.</summary>
public sealed record PayrollSizeLine(string SizeCode, int Qty, decimal Rate, decimal Subtotal);

public sealed record PayrollWoGroup(
    Guid WoId,
    string WoCode,
    string ProcessName,
    IReadOnlyList<PayrollSizeLine> Lines,
    decimal Subtotal);

public sealed record PayrollDayGroup(DateOnly WorkDate, IReadOnlyList<PayrollDailyRow> Rows, decimal Subtotal);

public sealed record PayrollWorkerDetailDto(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    int TotalQty,
    decimal SumBeforeRounding,
    decimal Paid,
    IReadOnlyList<PayrollWoGroup> ByWo,
    IReadOnlyList<PayrollDayGroup> ByDay,
    IReadOnlyList<PayrollDailyRow> Adjustments);

public sealed record PayrollMatrixCell(DateOnly Date, int Qty, decimal Amount);

public sealed record PayrollMatrixRow(
    Guid WorkerId,
    string WorkerCode,
    string WorkerName,
    IReadOnlyList<PayrollMatrixCell> Cells,
    int TotalQty,
    decimal TotalAmount);

public sealed record PayrollMatrixDto(
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<PayrollMatrixRow> Rows,
    IReadOnlyList<PayrollMatrixCell> DayTotals);

public interface IPayrollApi
{
    Task<IReadOnlyList<PayrollPeriodListItem>> ListPeriodsAsync(CancellationToken ct = default);
    Task<NextPeriodDefaultsDto> GetNextPeriodDefaultsAsync(CancellationToken ct = default);
    Task<PayrollPeriodDto> CreatePeriodAsync(SavePeriodRequest request, CancellationToken ct = default);
    Task<PayrollPeriodDto> GetPeriodAsync(Guid id, CancellationToken ct = default);
    Task<PayrollPeriodDto> UpdatePeriodAsync(Guid id, SavePeriodRequest request, CancellationToken ct = default);
    Task<ClosePreviewDto> PreviewClosePeriodAsync(Guid id, CancellationToken ct = default);
    Task<PayrollPeriodDto> ClosePeriodAsync(Guid id, ClosePeriodRequest request, CancellationToken ct = default);

    Task<PayrollSummaryDto> GetSummaryAsync(Guid periodId, string? q, CancellationToken ct = default);
    Task<PayrollWorkerDetailDto> GetWorkerDetailAsync(Guid periodId, Guid workerId, CancellationToken ct = default);
    Task<PayrollDailyResult> GetDailyAsync(Guid periodId, PayrollDailyQuery query, CancellationToken ct = default);
    Task<PayrollMatrixDto> GetMatrixAsync(Guid periodId, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollDailyRow>> GetAdjustmentsAsync(Guid periodId, CancellationToken ct = default);

    Task<FileContent> ExportAsync(Guid periodId, PayrollExportFormat format, CancellationToken ct = default);

    /// <summary><paramref name="workerId"/> null = semua pekerja dalam satu berkas.</summary>
    Task<FileContent> GetSlipsAsync(Guid periodId, Guid? workerId, bool includeDaily, CancellationToken ct = default);
}
