namespace Gms.Domain.Common;

/// <summary>Kolom jejak pembuatan/perubahan.</summary>
public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Entitas dengan token konkurensi optimistis. <see cref="RowVersion"/> dipetakan ke kolom sistem PostgreSQL <c>xmin</c>
/// (lihat Fluent API). Konflik menghasilkan Problem Details <c>concurrency-conflict</c>.
/// </summary>
public abstract class VersionedEntity : AuditableEntity
{
    public uint RowVersion { get; set; }
}

/// <summary>Dasar semua tabel master: kunci GUID v7 yang dibuat aplikasi dan status aktif (tidak ada hard delete).</summary>
public abstract class MasterEntity : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public bool IsActive { get; set; } = true;
}

/// <summary>Penghitung nomor dokumen (ADR-007): key mis. <c>PO-202611</c>, <c>CUS</c>.</summary>
public sealed class NumberSequence
{
    public string Key { get; set; } = string.Empty;
    public long NextValue { get; set; }
}

/// <summary>Entitas master yang memiliki kode unik (tanpa peka huruf besar-kecil) dan nama.</summary>
public interface ICodedEntity
{
    string Code { get; set; }
    string Name { get; set; }
}
