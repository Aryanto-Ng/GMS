using Gms.Shared.Enums;

namespace Gms.Domain.Inventory;

/// <summary>Serah terima FG Konveksi → Comfi (satu langkah).</summary>
public sealed class FgHandover
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WoId { get; set; }
    public DateOnly HandoverDate { get; set; }
    public string? DocNo { get; set; }
    public string? ReceivedBy { get; set; }
    public string? Notes { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Active;
    public Guid? ReplacesId { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public ICollection<FgHandoverLine> Lines { get; set; } = [];
}

public sealed class FgHandoverLine
{
    public Guid HandoverId { get; set; }
    public Guid WoLineId { get; set; }
    public int Qty { get; set; }

    /// <summary>Bagian yang dialokasikan otomatis ke baris PO asal (untuk pembalikan saat void).</summary>
    public int AutoAllocatedQty { get; set; }
}

/// <summary>Proyeksi stok FG per FG × Color × Size. Tersedia = OnHand − Allocated.</summary>
public sealed class StockBalance
{
    public Guid FgId { get; set; }
    public Guid ColorId { get; set; }
    public Guid SizeId { get; set; }
    public int OnHand { get; set; }
    public int Allocated { get; set; }

    public int Available => OnHand - Allocated;
}

/// <summary>Ledger stok FG (append-only). Qty bertanda.</summary>
public sealed class StockLedgerEntry
{
    public long Id { get; set; }
    public Guid FgId { get; set; }
    public Guid ColorId { get; set; }
    public Guid SizeId { get; set; }
    public StockLedgerKind Kind { get; set; }
    public int Qty { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public DateOnly EntryDate { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>Catatan serah terima material Comfi → Konveksi (tanpa stok material).</summary>
public sealed class MaterialHandover
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WoId { get; set; }
    public DateOnly HandoverDate { get; set; }
    public string? DocNo { get; set; }
    public string? Notes { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Active;
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public ICollection<MaterialHandoverLine> Lines { get; set; } = [];
}

public sealed class MaterialHandoverLine
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid HandoverId { get; set; }
    public Guid MaterialId { get; set; }

    /// <summary>Maksimal 3 digit desimal.</summary>
    public decimal Qty { get; set; }

    /// <summary>Snapshot kode satuan.</summary>
    public string UomCode { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
