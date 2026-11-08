using System.Reflection;
using Gms.Api.Features.Access.Seeding;
using Gms.Api.Hosting;
using Gms.Api.Security;
using Gms.Api.Tests.Infrastructure;
using Gms.Domain.Access;
using Gms.Infrastructure.Identity;
using Gms.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gms.Api.Tests.Access;

[Collection(ApiCollection.Name)]
public sealed class SeederTests(ApiFixture fixture)
{
    private TestHost Host => fixture.Main;

    private static IEnumerable<string> AllPermissionConstants()
    {
        static IEnumerable<string> Walk(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                         .Where(f => f.IsLiteral && f.FieldType == typeof(string)))
            {
                yield return (string)field.GetRawConstantValue()!;
            }

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
            {
                foreach (var value in Walk(nested))
                {
                    yield return value;
                }
            }
        }

        return Walk(typeof(Permissions));
    }

    [Fact]
    public void Setiap_konstanta_Permissions_terdaftar_di_katalog_dan_sebaliknya()
    {
        var constants = AllPermissionConstants().ToHashSet();
        var catalog = PermissionCatalog.All.Select(p => p.Key).ToHashSet();

        Assert.Empty(constants.Except(catalog));
        Assert.Empty(catalog.Except(constants));
        Assert.Equal(PermissionCatalog.All.Count, catalog.Count);
    }

    [Fact]
    public void Hanya_permission_AccessManagement_yang_tidak_dapat_diberikan()
    {
        var nonAssignable = PermissionCatalog.All.Where(p => !p.IsAssignable).ToList();

        Assert.NotEmpty(nonAssignable);
        Assert.All(nonAssignable, p => Assert.StartsWith("AccessManagement.", p.Key));
        Assert.All(PermissionCatalog.All.Where(p => p.Key.StartsWith("AccessManagement.")), p => Assert.False(p.IsAssignable));
    }

    [Fact]
    public void Aturan_View_wajib_dipetakan_ke_resource_yang_sama()
    {
        Assert.Equal("WorkOrder.View", PermissionCatalog.RequiredViewFor("WorkOrder.Release"));
        Assert.Equal("MasterData.Material.View", PermissionCatalog.RequiredViewFor("MasterData.Material.Import"));
        Assert.Equal("CustomerPO.Alokasi.View", PermissionCatalog.RequiredViewFor("CustomerPO.Alokasi.Allocate"));
        Assert.Null(PermissionCatalog.RequiredViewFor("WorkOrder.View"));
    }

    [Fact]
    public async Task Seed_membuat_katalog_role_Administrator_dan_satu_admin_awal()
    {
        await using var db = Host.CreateDbContext();

        Assert.Equal(PermissionCatalog.All.Count, await db.Permissions.CountAsync());
        var admin = await db.Roles.SingleAsync(r => r.IsSystem);
        Assert.Equal(AccessSeeder.AdministratorRoleName, admin.Name);
        Assert.Equal(PermissionCatalog.All.Count, await db.RolePermissions.CountAsync(rp => rp.RoleId == admin.Id));
        Assert.True(await db.Users.AnyAsync(u => u.UserName == "admin" && u.RoleId == admin.Id));
    }

    [Fact]
    public async Task Seed_idempoten_dijalankan_ulang_tanpa_duplikasi()
    {
        await using var before = Host.CreateDbContext();
        var permissionsBefore = await before.Permissions.CountAsync();
        var adminsBefore = await before.Users.CountAsync(u => u.UserName == "admin");
        var rolesBefore = await before.Roles.CountAsync(r => r.IsSystem);

        using var scope = Host.Services.CreateScope();
        await DatabaseInitializer.SeedAsync(scope.ServiceProvider);
        await DatabaseInitializer.SeedAsync(scope.ServiceProvider);

        await using var after = Host.CreateDbContext();
        Assert.Equal(permissionsBefore, await after.Permissions.CountAsync());
        Assert.Equal(adminsBefore, await after.Users.CountAsync(u => u.UserName == "admin"));
        Assert.Equal(rolesBefore, await after.Roles.CountAsync(r => r.IsSystem));
    }

    [Fact]
    public async Task Seed_tidak_membuat_admin_lagi_atau_mengubah_password_admin_yang_ada()
    {
        // Password bootstrap sudah diganti; seeder tidak boleh mengembalikannya.
        using var scope = Host.Services.CreateScope();
        await DatabaseInitializer.SeedAsync(scope.ServiceProvider);

        var stillWorks = await Host.Api.PostAsync("/api/v1/auth/login",
            new Gms.Shared.Access.LoginRequest("admin", TestHost.BootstrapFinalPassword));
        Assert.True(stillWorks.IsSuccess, stillWorks.Body);

        // Login ini mencabut sesi bootstrap; terbitkan token baru supaya tes lain tetap valid.
        await Host.RefreshAdminTokenAsync();
    }

    [Fact]
    public async Task Seed_mengembalikan_permission_yang_hilang_pada_Administrator_tanpa_menyentuh_role_lain()
    {
        var other = await Host.CreateRoleAsync(null, Permissions.CustomerPO.View, Permissions.WorkOrder.View);

        await using (var db = Host.CreateDbContext())
        {
            var adminRoleId = await db.Roles.Where(r => r.IsSystem).Select(r => r.Id).SingleAsync();
            await db.RolePermissions
                .Where(rp => rp.RoleId == adminRoleId && rp.PermissionKey == Permissions.Payroll.View)
                .ExecuteDeleteAsync();
            await db.Permissions.Where(p => p.Key == Permissions.Payroll.View)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.Description, "diubah manual"));
        }

        using var scope = Host.Services.CreateScope();
        await DatabaseInitializer.SeedAsync(scope.ServiceProvider);

        await using var check = Host.CreateDbContext();
        var adminId = await check.Roles.Where(r => r.IsSystem).Select(r => r.Id).SingleAsync();
        Assert.True(await check.RolePermissions.AnyAsync(rp => rp.RoleId == adminId && rp.PermissionKey == Permissions.Payroll.View));
        Assert.NotEqual("diubah manual", await check.Permissions.Where(p => p.Key == Permissions.Payroll.View).Select(p => p.Description).SingleAsync());

        var otherKeys = await check.RolePermissions.Where(rp => rp.RoleId == other.Id).Select(rp => rp.PermissionKey).OrderBy(k => k).ToListAsync();
        Assert.Equal([Permissions.CustomerPO.View, Permissions.WorkOrder.View], otherKeys);
    }

    [Fact]
    public async Task Seed_pada_database_kosong_tanpa_password_gagal_dengan_pesan_jelas()
    {
        var iso = await fixture.CreateIsolatedAsync();
        await using var db = iso.CreateDbContext();
        await db.UserSessions.ExecuteDeleteAsync();
        await db.Users.ExecuteDeleteAsync();

        using var scope = iso.Services.CreateScope();
        var seeder = new AccessSeeder(
            scope.ServiceProvider.GetRequiredService<Gms.Infrastructure.Persistence.GmsDbContext>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            Options.Create(new SeedOptions { AdminUserName = "admin", AdminPassword = null }),
            TimeProvider.System,
            NullLogger<AccessSeeder>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(CancellationToken.None));
        Assert.Contains("Seed:AdminPassword", ex.Message);
    }
}
