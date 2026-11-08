using Gms.Api.Security;
using Gms.Shared.Access;
using Gms.Shared.Security;

namespace Gms.Api.Features.Access.Roles;

public static class RolesEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/roles").WithTags("Role");

        group.MapGet("", async (ListRolesHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct)))
            .RequirePermission(Permissions.AccessManagement.Role.View);

        group.MapGet("/{id:guid}", async (Guid id, GetRoleHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, ct)))
            .RequirePermission(Permissions.AccessManagement.Role.View);

        group.MapPost("", async (SaveRoleRequest request, CreateRoleHandler handler, CancellationToken ct) =>
        {
            var created = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/roles/{created.Id}", created);
        }).RequirePermission(Permissions.AccessManagement.Role.Create);

        group.MapPut("/{id:guid}", async (Guid id, SaveRoleRequest request, UpdateRoleHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct)))
            .RequirePermission(Permissions.AccessManagement.Role.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, DeleteRoleHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AccessManagement.Role.Delete);

        app.MapGet("/api/v1/permissions", async (GetPermissionCatalogHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(ct)))
            .WithTags("Role")
            .RequirePermission(Permissions.AccessManagement.Role.View);
    }
}
