using Gms.Api.Hosting;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.Master;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gms.Api.Tests.Master;

[Collection(ApiCollection.Name)]
public sealed class MasterSeederTests(ApiFixture fixture)
{
    [Fact]
    public async Task Customer_STOK_INTERNAL_ada_tepat_satu_dan_tetap_satu_setelah_seeder_diulang()
    {
        var host = fixture.Main;
        using (var scope = host.Services.CreateScope())
        {
            await DatabaseInitializer.SeedAsync(scope.ServiceProvider);
            await DatabaseInitializer.SeedAsync(scope.ServiceProvider);
        }

        await using var db = host.CreateDbContext();
        var customers = await db.Customers.Where(c => c.Name == "STOK INTERNAL").ToListAsync();
        Assert.Single(customers);
        Assert.Matches(@"^CUS-\d{4}$", customers[0].Code);
        Assert.True(customers[0].IsActive);
    }

    [Fact]
    public async Task Tanpa_DemoData_database_tidak_berisi_data_contoh()
    {
        var host = await fixture.CreateIsolatedAsync();
        await using var db = host.CreateDbContext();

        Assert.False(await db.Fgs.AnyAsync());
        Assert.False(await db.Sizes.AnyAsync());
        Assert.False(await db.Workers.AnyAsync());
        Assert.Equal(1, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task DemoData_membuat_master_FG_lengkap_dan_satu_FG_belum_siap_serta_idempoten()
    {
        var host = await fixture.CreateIsolatedAsync(new Dictionary<string, string?> { ["Seed:DemoData"] = "true" });

        var ready = (await host.Api.GetAsync("/api/v1/master/fgs?readyOnly=true&pageSize=50", host.AdminToken)).Ok().As<PagedResult<FgListItem>>();
        Assert.Equal(["CLN-001", "KMJ-001"], ready.Items.Select(f => f.Code).Order());

        var all = (await host.Api.GetAsync("/api/v1/master/fgs?pageSize=50", host.AdminToken)).Ok().As<PagedResult<FgListItem>>();
        Assert.False(all.Items.Single(f => f.Code == "COV-001").IsReady);

        var kmj = all.Items.Single(f => f.Code == "KMJ-001");
        var grid = (await host.Api.GetAsync($"/api/v1/master/fgs/{kmj.Id}/tariffs", host.AdminToken)).Ok().As<FgTariffGridDto>();
        Assert.Equal(4, grid.InternalSteps.Count);
        Assert.Single(grid.VendorSteps);
        Assert.Equal(5, grid.Sizes.Count);
        Assert.All(grid.Cells, c => Assert.True(c.Rate > 0));
        Assert.True(grid.Cells.Select(c => c.Rate).Distinct().Count() > 5, "tarif berbeda per proses dan per size");

        await using var db = host.CreateDbContext();
        var counts = (await db.Fgs.CountAsync(), await db.Workers.CountAsync(), await db.Sizes.CountAsync(), await db.Customers.CountAsync());

        using (var scope = host.Services.CreateScope())
        {
            await DatabaseInitializer.SeedAsync(scope.ServiceProvider);
        }

        await using var again = host.CreateDbContext();
        Assert.Equal(counts, (await again.Fgs.CountAsync(), await again.Workers.CountAsync(), await again.Sizes.CountAsync(), await again.Customers.CountAsync()));
        Assert.Equal((3, 6, 6, 3), counts);
    }
}
