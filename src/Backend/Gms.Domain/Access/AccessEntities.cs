using Gms.Domain.Common;
using Gms.Shared.Enums;

namespace Gms.Domain.Access;

/// <summary>Role aplikasi (bukan Identity Role). Satu pengguna memiliki tepat satu Role.</summary>
public sealed class AppRole : VersionedEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>True = Administrator (tidak dapat diubah/dihapus).</summary>
    public bool IsSystem { get; set; }

    public ICollection<RolePermission> Permissions { get; set; } = [];
}

/// <summary>Katalog permission (hasil seed dari <c>PermissionCatalog</c>).</summary>
public sealed class Permission
{
    public string Key { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsAssignable { get; set; } = true;
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}

/// <summary>Sesi opaque. Hanya hash token yang disimpan. Satu sesi aktif per pengguna (indeks unik parsial).</summary>
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public SessionRevokedReason? RevokedReason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>Audit log append-only (trigger database melarang UPDATE/DELETE).</summary>
public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorUsername { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }

    /// <summary>JSON (jsonb). Dilarang memuat password/hash/token.</summary>
    public string? OldValue { get; set; }

    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
}
