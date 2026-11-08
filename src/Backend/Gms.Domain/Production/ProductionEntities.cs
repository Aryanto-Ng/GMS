using Gms.Domain.Common;
using Gms.Shared.Enums;

namespace Gms.Domain.Production;

public sealed class WorkOrder : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = string.Empty;

    /// <summary>1 WO = 1 PO.</summary>
    public Guid PoId { get; set; }

    /// <summary>1 WO = 1 FG.</summary>
    public Guid FgId { get; set; }

    // Snapshot label (dikunci saat Release)
    public string FgCode { get; set; } = string.Empty;
    public string FgName { get; set; } = string.Empty;

    public DateOnly? StartDate { get; set; }
    public DateOnly? TargetDate { get; set; }
    public WoStatus Status { get; set; } = WoStatus.Draft;
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }
    public Guid? ReleasedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }

    public ICollection<WoLine> Lines { get; set; } = [];
    public ICollection<WoStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<WoRoutingSnapshot> RoutingSnapshot { get; set; } = [];
    public ICollection<WoTariffSnapshot> TariffSnapshot { get; set; } = [];
}

public sealed class WoLine : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WoId { get; set; }
    public Guid PoLineId { get; set; }
    public int Qty { get; set; }

    /// <summary>Proyeksi: total serah terima FG aktif untuk baris ini.</summary>
    public int HandedOverQty { get; set; }

    // Snapshot label
    public string ColorCode { get; set; } = string.Empty;
    public string ColorName { get; set; } = string.Empty;
    public string SizeCode { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;
}

public sealed class WoStatusHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WoId { get; set; }
    public WoStatus? FromStatus { get; set; }
    public WoStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; }
}

/// <summary>Snapshot routing yang dibuat saat Release. Tidak pernah diubah.</summary>
public sealed class WoRoutingSnapshot
{
    public Guid WoId { get; set; }
    public int Seq { get; set; }
    public Guid ProcessId { get; set; }
    public string ProcessCode { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public ProcessType ProcessType { get; set; }
}

/// <summary>Snapshot tarif (langkah Internal × Size) yang dibuat saat Release. Dasar perhitungan upah. Tidak pernah diubah.</summary>
public sealed class WoTariffSnapshot
{
    public Guid WoId { get; set; }
    public int Seq { get; set; }
    public Guid SizeId { get; set; }
    public string SizeCode { get; set; } = string.Empty;
    public decimal Rate { get; set; }
}

/// <summary>
/// Proyeksi progres per (baris WO × langkah). <see cref="Qty"/> = output kumulatif (Internal) atau diterima (Vendor);
/// <see cref="SentQty"/> = terkirim ke vendor (langkah Vendor).
/// </summary>
public sealed class WoStepBalance
{
    public Guid WoLineId { get; set; }
    public int Seq { get; set; }
    public int Qty { get; set; }
    public int SentQty { get; set; }
}

/// <summary>Ledger output pekerja. Hanya metadata status yang berubah (void). Qty tidak pernah diubah.</summary>
public sealed class OutputEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WoId { get; set; }
    public int Seq { get; set; }
    public Guid WorkerId { get; set; }
    public DateOnly WorkDate { get; set; }
    public OutputKind Kind { get; set; } = OutputKind.Output;
    public DocStatus Status { get; set; } = DocStatus.Active;

    /// <summary>Wajib untuk Adjustment: tanggal kerja asli pada periode gaji tertutup.</summary>
    public DateOnly? OriginalDate { get; set; }

    public Guid? ReplacesEntryId { get; set; }
    public Guid? RefEntryId { get; set; }
    public string? Reason { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public ICollection<OutputEntryLine> Lines { get; set; } = [];
}

public sealed class OutputEntryLine
{
    public Guid EntryId { get; set; }
    public Guid WoLineId { get; set; }

    /// <summary>Tidak boleh 0. Negatif hanya sah untuk entri Adjustment.</summary>
    public int Qty { get; set; }
}

/// <summary>Kirim/terima vendor (langkah bertipe Vendor).</summary>
public sealed class VendorMovement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WoId { get; set; }
    public int Seq { get; set; }
    public Guid VendorId { get; set; }
    public VendorMovementKind Kind { get; set; }
    public DateOnly MovementDate { get; set; }
    public string? DocNo { get; set; }
    public string? Notes { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Active;
    public Guid? ReplacesId { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public ICollection<VendorMovementLine> Lines { get; set; } = [];
}

public sealed class VendorMovementLine
{
    public Guid MovementId { get; set; }
    public Guid WoLineId { get; set; }
    public int Qty { get; set; }
}
