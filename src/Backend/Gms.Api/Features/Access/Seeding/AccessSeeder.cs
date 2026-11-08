using Gms.Api.Hosting;
using Gms.Api.Security;
using Gms.Domain.Access;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Features.Access.Seeding;

/// <summary>
/// Seeder Access Management (idempoten, AC-AM-27/28):
/// 1) katalog permission (menambah yang baru, memperbarui metadata, tidak menyentuh Role selain Administrator);
/// 2) Role Administrator (sistem) dengan SEMUA permission, termasuk permission baru dari modul berikutnya;
/// 3) akun Administrator awal hanya bila belum ada pengguna ber-Role Administrator.
/// </summary>
public sealed class AccessSeeder(
    GmsDbContext db,
    UserManager<AppUser> users,
    IOptions<SeedOptions> seedOptions,
    TimeProvider time,
    ILogger<AccessSeeder> logger) : IDataSeeder
{
    public const string AdministratorRoleName = "Administrator";

    public int Order => 10;

    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedPermissionsAsync(ct);
        var role = await EnsureAdministratorRoleAsync(ct);
        await GrantAllPermissionsAsync(role, ct);
        await EnsureAdministratorUserAsync(role, ct);
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, ct);

        foreach (var def in PermissionCatalog.All)
        {
            if (!existing.TryGetValue(def.Key, out var row))
            {
                db.Permissions.Add(new Permission
                {
                    Key = def.Key,
                    Resource = def.Resource,
                    Action = def.Action,
                    Module = def.Module,
                    Description = def.Description,
                    SortOrder = def.SortOrder,
                    IsAssignable = def.IsAssignable,
                });
                continue;
            }

            // Metadata tampilan boleh diperbarui; kunci tidak pernah berubah.
            row.Resource = def.Resource;
            row.Action = def.Action;
            row.Module = def.Module;
            row.Description = def.Description;
            row.SortOrder = def.SortOrder;
            row.IsAssignable = def.IsAssignable;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<AppRole> EnsureAdministratorRoleAsync(CancellationToken ct)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.IsSystem, ct);
        if (role is not null)
        {
            return role;
        }

        role = new AppRole
        {
            Name = AdministratorRoleName,
            Description = "Akses penuh ke seluruh modul (role sistem).",
            IsSystem = true,
            CreatedAt = time.GetUtcNow(),
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role;
    }

    private async Task GrantAllPermissionsAsync(AppRole role, CancellationToken ct)
    {
        var granted = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Select(rp => rp.PermissionKey)
            .ToHashSetAsync(StringComparer.Ordinal, ct);

        var missing = await db.Permissions.Select(p => p.Key).Where(k => !granted.Contains(k)).ToListAsync(ct);
        foreach (var key in missing)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
        }

        if (missing.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task EnsureAdministratorUserAsync(AppRole role, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.RoleId == role.Id, ct))
        {
            return;
        }

        var options = seedOptions.Value;
        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            throw new InvalidOperationException(
                "Belum ada Administrator dan Seed:AdminPassword belum dikonfigurasi. " +
                "Set lewat user-secrets (dev) atau environment variable Seed__AdminPassword; jangan menaruhnya di repository.");
        }

        var admin = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = options.AdminUserName.Trim(),
            FullName = options.AdminFullName,
            RoleId = role.Id,
            IsActive = true,
            MustChangePassword = true,
            LockoutEnabled = true,
            CreatedAt = time.GetUtcNow(),
        };

        var result = await users.CreateAsync(admin, options.AdminPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Gagal membuat Administrator awal: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        logger.LogInformation("Administrator awal '{UserName}' dibuat (wajib ganti password saat login pertama).", admin.UserName);
    }
}
