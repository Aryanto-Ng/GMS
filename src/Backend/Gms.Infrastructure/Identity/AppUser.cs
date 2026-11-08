using Microsoft.AspNetCore.Identity;

namespace Gms.Infrastructure.Identity;

/// <summary>
/// Pengguna aplikasi (ASP.NET Core Identity). Satu pengguna memiliki tepat satu Role (FSD Access Management Q1).
/// Role dan permission adalah entitas domain sendiri (bukan Identity Role) agar matriks Resource x Action
/// dapat dikelola langsung. Lihat docs/Architecture/Database.md.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>FK ke app_roles. Wajib.</summary>
    public Guid RoleId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Wajib ganti password pada login berikutnya (akun baru atau hasil reset).</summary>
    public bool MustChangePassword { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Token konkurensi optimistis (kolom sistem xmin).</summary>
    public uint RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
