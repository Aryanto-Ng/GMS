using Gms.Domain.Common;
using Gms.Shared.Enums;

namespace Gms.Domain.Payroll;

public sealed class PayrollPeriod : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public PayrollStatus Status { get; set; } = PayrollStatus.Open;
    public string? Notes { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }

    /// <summary>Terisi saat Closed (hasil snapshot).</summary>
    public decimal? TotalPaid { get; set; }
}

/// <summary>Baris rincian beku saat periode ditutup.</summary>
public sealed class PayrollSnapshotLine
{
    public long Id { get; set; }
    public Guid PeriodId { get; set; }
    public Guid WorkerId { get; set; }
    public string WorkerCode { get; set; } = string.Empty;
    public string WorkerName { get; set; } = string.Empty;
    public Guid WoId { get; set; }
    public string WoCode { get; set; } = string.Empty;
    public string ProcessCode { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string SizeCode { get; set; } = string.Empty;
    public DateOnly WorkDate { get; set; }
    public OutputKind Kind { get; set; }
    public DateOnly? OriginalDate { get; set; }
    public string? Reason { get; set; }
    public int Qty { get; set; }
    public decimal Rate { get; set; }
    public decimal Subtotal { get; set; }
    public Guid SourceEntryId { get; set; }
}

/// <summary>Total per pekerja yang dibekukan saat periode ditutup.</summary>
public sealed class PayrollSnapshotWorker
{
    public Guid PeriodId { get; set; }
    public Guid WorkerId { get; set; }
    public string WorkerCode { get; set; } = string.Empty;
    public string WorkerName { get; set; } = string.Empty;
    public int TotalQty { get; set; }
    public decimal SumBeforeRounding { get; set; }

    /// <summary>Dibulatkan sekali per pekerja (setengah ke atas menjauhi nol).</summary>
    public decimal PaidAmount { get; set; }
}
