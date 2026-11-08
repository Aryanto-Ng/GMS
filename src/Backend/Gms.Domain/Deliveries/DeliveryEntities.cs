using Gms.Domain.Common;
using Gms.Shared.Enums;

namespace Gms.Domain.Deliveries;

/// <summary>Surat jalan: satu surat jalan untuk satu PO.</summary>
public sealed class Delivery : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = string.Empty;
    public Guid PoId { get; set; }
    public DateOnly DeliveryDate { get; set; }
    public string? RecipientName { get; set; }
    public string? Vehicle { get; set; }
    public string? Driver { get; set; }
    public string? Notes { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Active;
    public Guid? ReplacesId { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }

    /// <summary>Cetakan ke-2 dst. diberi penanda "CETAK ULANG".</summary>
    public int PrintCount { get; set; }

    public ICollection<DeliveryLine> Lines { get; set; } = [];
}

public sealed class DeliveryLine
{
    public Guid DeliveryId { get; set; }
    public Guid PoLineId { get; set; }
    public int Qty { get; set; }
}
