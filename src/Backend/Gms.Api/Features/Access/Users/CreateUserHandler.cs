using System.Text.RegularExpressions;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Identity;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Users;

/// <summary>Membuat pengguna (AC-AM-10). Password awal wajib diganti pada login pertama (AC-AM-08).</summary>
public sealed partial class CreateUserHandler(
    GmsDbContext db,
    UserManager<AppUser> users,
    ICurrentUser current,
    IAuditWriter audit,
    TimeProvider time)
{
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex UserNamePattern();

    public async Task<UserDetail> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        var userName = request.UserName?.Trim() ?? string.Empty;
        var fullName = request.FullName?.Trim() ?? string.Empty;

        if (userName.Length is < 3 or > 50)
        {
            errors.Add("userName", "Username harus 3 sampai 50 karakter.");
        }
        else if (!UserNamePattern().IsMatch(userName))
        {
            errors.Add("userName", "Username hanya boleh berisi huruf, angka, titik, strip, atau garis bawah.");
        }

        if (fullName.Length is 0 or > 200)
        {
            errors.Add("fullName", "Nama lengkap wajib diisi (maksimal 200 karakter).");
        }

        if (string.IsNullOrEmpty(request.InitialPassword))
        {
            errors.Add("initialPassword", "Password awal wajib diisi.");
        }

        var role = await db.Roles.AsNoTracking()
            .Where(r => r.Id == request.RoleId)
            .Select(r => new { r.Id, r.Name })
            .SingleOrDefaultAsync(ct);
        if (role is null)
        {
            errors.Add("roleId", "Role tidak ditemukan.");
        }

        errors.ThrowIfAny();
        await SystemRoleGuard.EnsureCanAssignRoleAsync(db, current, role!.Id, ct);

        var now = time.GetUtcNow();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            FullName = fullName,
            RoleId = role!.Id,
            IsActive = true,
            MustChangePassword = true,
            LockoutEnabled = true,
            CreatedAt = now,
            CreatedBy = current.Id,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var result = await users.CreateAsync(user, request.InitialPassword);
        if (!result.Succeeded)
        {
            throw IdentityErrorMapper.ToProblem(result.Errors, "initialPassword");
        }

        audit.Add(AuditEvents.UserCreated, "User", user.Id.ToString(),
            newValue: new { userName, fullName, role = role.Name });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await UserQueries.GetDetailAsync(db, user.Id, now, ct);
    }
}
