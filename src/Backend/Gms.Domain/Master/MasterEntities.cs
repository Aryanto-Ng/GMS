using Gms.Domain.Common;
using Gms.Shared.Enums;

namespace Gms.Domain.Master;

public sealed class Uom : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class MaterialCategory : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class Material : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public Guid UomId { get; set; }
    public string? Description { get; set; }
}

public sealed class Size : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class Color : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Format <c>#RRGGBB</c>, opsional.</summary>
    public string? Hex { get; set; }
}

public sealed class Customer : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public sealed class Process : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ProcessType Type { get; set; }
}

public sealed class Vendor : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ServiceType { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Pekerja borongan (dikelola Konveksi).</summary>
public sealed class Worker : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateOnly? JoinDate { get; set; }

    /// <summary>Hanya untuk memudahkan pencarian; tidak membatasi proses yang boleh dikerjakan.</summary>
    public Guid? MainProcessId { get; set; }

    public string? Notes { get; set; }
}

public sealed class Fg : MasterEntity, ICodedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Kind { get; set; }
    public Guid? CustomerId { get; set; }
    public string? Notes { get; set; }

    public ICollection<FgSize> Sizes { get; set; } = [];
    public ICollection<FgRoutingStep> RoutingSteps { get; set; } = [];
    public ICollection<FgTariff> Tariffs { get; set; } = [];
}

/// <summary>Size Berlaku sebuah FG.</summary>
public sealed class FgSize
{
    public Guid FgId { get; set; }
    public Guid SizeId { get; set; }
}

/// <summary>Satu langkah routing (linear). Satu Process hanya sekali per FG.</summary>
public sealed class FgRoutingStep
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FgId { get; set; }
    public Guid ProcessId { get; set; }
    public int Seq { get; set; }
}

/// <summary>Tarif per FG × Proses (Internal) × Size, presisi 5 desimal, harus &gt; 0.</summary>
public sealed class FgTariff
{
    public Guid FgId { get; set; }
    public Guid ProcessId { get; set; }
    public Guid SizeId { get; set; }
    public decimal Rate { get; set; }
}
