using Gms.Domain.Common;
using Gms.Shared.Enums;

namespace Gms.Domain.Sales;

public sealed class PurchaseOrder : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }

    /// <summary>Boleh kosong saat PO internal dibuka; diisi kemudian.</summary>
    public string? CustomerPoNumber { get; set; }

    public DateOnly PoDate { get; set; }
    public PoStatus Status { get; set; } = PoStatus.Draft;
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }

    public ICollection<PoLine> Lines { get; set; } = [];
}

public sealed class PoLine : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PoId { get; set; }
    public Guid FgId { get; set; }
    public Guid ColorId { get; set; }
    public Guid SizeId { get; set; }
    public DateOnly DeliveryDate { get; set; }
    public int Qty { get; set; }
    public string? Notes { get; set; }

    // Proyeksi (ADR-004). Diperbarui dalam transaksi yang sama dengan ledger; dijaga CHECK di database.
    public int DeliveredQty { get; set; }
    public int AllocatedQty { get; set; }
    public int InProductionQty { get; set; }

    /// <summary>Sisa Rencana = Qty − Terkirim − Dialokasikan − Dalam Produksi.</summary>
    public int RemainingPlanQty => Qty - DeliveredQty - AllocatedQty - InProductionQty;
}

/// <summary>Log alokasi stok ke baris PO (append-only).</summary>
public sealed class PoAllocation
{
    public long Id { get; set; }
    public Guid PoLineId { get; set; }
    public AllocationKind Kind { get; set; }
    public int Qty { get; set; }
    public AllocationSource Source { get; set; }
    public Guid? SourceId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
