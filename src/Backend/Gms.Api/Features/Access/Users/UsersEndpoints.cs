using Gms.Api.Security;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Gms.Shared.Security;

namespace Gms.Api.Features.Access.Users;

public static class UsersEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Pengguna");

        group.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, Guid? roleId, UserStatus? status,
            ListUsersHandler handler, CancellationToken ct) =>
        {
            var query = new UserListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort, RoleId = roleId, Status = status,
            };
            return Results.Ok(await handler.HandleAsync(query, ct));
        }).RequirePermission(Permissions.AccessManagement.User.View);

        group.MapGet("/{id:guid}", async (Guid id, GetUserHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, ct)))
            .RequirePermission(Permissions.AccessManagement.User.View);

        group.MapPost("", async (CreateUserRequest request, CreateUserHandler handler, CancellationToken ct) =>
        {
            var created = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/users/{created.Id}", created);
        }).RequirePermission(Permissions.AccessManagement.User.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, UpdateUserHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct)))
            .RequirePermission(Permissions.AccessManagement.User.Edit);

        group.MapPost("/{id:guid}/reset-password", async (Guid id, ResetPasswordRequest request, ResetPasswordHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct)))
            .RequirePermission(Permissions.AccessManagement.User.ResetPassword);

        group.MapPost("/{id:guid}/unlock", async (Guid id, UnlockUserHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AccessManagement.User.Unlock);

        group.MapPost("/{id:guid}/end-session", async (Guid id, EndUserSessionHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AccessManagement.User.EndSession);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, uint rowVersion, SetUserActiveHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, false, rowVersion, ct)))
            .RequirePermission(Permissions.AccessManagement.User.Deactivate);

        group.MapPost("/{id:guid}/activate", async (Guid id, uint rowVersion, SetUserActiveHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, true, rowVersion, ct)))
            .RequirePermission(Permissions.AccessManagement.User.Deactivate);
    }
}
