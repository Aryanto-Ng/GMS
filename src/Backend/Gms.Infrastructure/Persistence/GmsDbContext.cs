using Gms.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gms.Infrastructure.Persistence;

/// <summary>
/// DbContext tunggal aplikasi (PostgreSQL). Identity tanpa tabel role bawaan (IdentityUserContext):
/// Role dan Permission adalah entitas domain (lihat docs/Architecture/Database.md).
/// Konfigurasi entitas ditaruh di kelas IEntityTypeConfiguration per modul (folder Configurations/).
/// </summary>
public sealed class GmsDbContext(DbContextOptions<GmsDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("btree_gist");

        builder.ApplyConfigurationsFromAssembly(typeof(GmsDbContext).Assembly);

        // Harus terakhir: menyesuaikan nama tabel/kolom yang belum eksplisit menjadi snake_case.
        SnakeCaseNaming.Apply(builder);
    }
}
