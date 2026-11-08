using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gms.Infrastructure.Persistence;

/// <summary>
/// Hanya dipakai oleh tool <c>dotnet ef</c> saat design-time (membuat migrasi tidak membutuhkan koneksi database).
/// Untuk perintah yang terkoneksi (<c>database update</c>) set environment variable <c>GMS_DESIGN_CONNECTION</c>;
/// jangan menaruh kredensial di repository.
/// </summary>
internal sealed class GmsDbContextFactory : IDesignTimeDbContextFactory<GmsDbContext>
{
    public GmsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("GMS_DESIGN_CONNECTION")
                         ?? "Host=localhost;Port=5432;Database=gms_design;Username=gms";

        var options = new DbContextOptionsBuilder<GmsDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new GmsDbContext(options);
    }
}
