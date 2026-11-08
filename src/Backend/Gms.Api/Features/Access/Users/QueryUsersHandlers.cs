using Gms.Api.Common;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Users;

public sealed class ListUsersHandler(GmsDbContext db, TimeProvider time)
{
    public async Task<PagedResult<UserListItem>> HandleAsync(UserListQuery query, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var users = db.Users.AsNoTracking().AsQueryable();

        if (query.RoleId is { } roleId)
        {
            users = users.Where(u => u.RoleId == roleId);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            users = users.Where(u => EF.Functions.ILike(u.UserName!, pattern) || EF.Functions.ILike(u.FullName, pattern));
        }

        users = query.Status switch
        {
            UserStatus.Inactive => users.Where(u => !u.IsActive),
            UserStatus.Locked => users.Where(u => u.IsActive && u.LockoutEnd != null && u.LockoutEnd > now),
            UserStatus.Active => users.Where(u => u.IsActive && (u.LockoutEnd == null || u.LockoutEnd <= now)),
            _ => users,
        };

        var projected = users
            .OrderBy(u => u.UserName)
            .Select(u => new
            {
                u.Id, u.UserName, u.FullName, u.RoleId, RoleName = db.Roles.Where(r => r.Id == u.RoleId).Select(r => r.Name).First(),
                u.IsActive, u.LockoutEnd, u.LastLoginAt,
            });

        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var total = await projected.CountAsync(ct);
        var rows = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var items = rows
            .Select(r => new UserListItem(
                r.Id, r.UserName ?? string.Empty, r.FullName, r.RoleId, r.RoleName,
                UserQueries.StatusOf(r.IsActive, r.LockoutEnd, now), r.LastLoginAt))
            .ToList();

        return new PagedResult<UserListItem>(items, page, pageSize, total);
    }
}

public sealed class GetUserHandler(GmsDbContext db, TimeProvider time)
{
    public Task<UserDetail> HandleAsync(Guid id, CancellationToken ct) =>
        UserQueries.GetDetailAsync(db, id, time.GetUtcNow(), ct);
}
