using Gms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Hosting;

/// <summary>
/// Seeder data awal. Setiap modul menambahkan implementasinya sendiri (permission, role, data demo, dst.).
/// WAJIB idempoten: dijalankan berulang tanpa duplikasi dan tanpa menimpa perubahan pengguna (AC-AM-28).
/// </summary>
public interface IDataSeeder
{
    /// <summary>Urutan eksekusi (kecil lebih dulu). Seeder Access = 10, Master = 20, dst.</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken ct);
}

public static class DatabaseInitializer
{
    /// <summary>
    /// Menjalankan migrasi dan/atau seeder sesuai konfigurasi <c>Database:MigrateOnStartup</c> dan
    /// <c>Database:SeedOnStartup</c> (bawaan false; Development menyalakannya). Produksi menjalankan migrasi secara terkendali.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            await scope.ServiceProvider.GetRequiredService<GmsDbContext>().Database.MigrateAsync(ct);
        }

        if (configuration.GetValue<bool>("Database:SeedOnStartup"))
        {
            await SeedAsync(scope.ServiceProvider, ct);
        }
    }

    public static async Task SeedAsync(IServiceProvider scopedServices, CancellationToken ct = default)
    {
        foreach (var seeder in scopedServices.GetServices<IDataSeeder>().OrderBy(s => s.Order))
        {
            await seeder.SeedAsync(ct);
        }
    }
}
