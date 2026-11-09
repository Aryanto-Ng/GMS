using Gms.Api.Security;
using Gms.Shared.Enums;
using Gms.Shared.Security;
using Gms.Shared.WorkOrders;

namespace Gms.Api.Features.WorkOrders;

public static class WoEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var wo = app.MapGroup("/api/v1/work-orders").WithTags("Work Order");

        wo.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, WoStatus? status, Guid? poId, Guid? customerId, Guid? fgId,
            DateOnly? from, DateOnly? to, bool? terlambat, bool? menungguClose, WoQueries queries, CancellationToken ct) =>
        {
            var query = new WoListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort, Status = status, PoId = poId,
                CustomerId = customerId, FgId = fgId, From = from, To = to,
                Terlambat = terlambat ?? false, MenungguClose = menungguClose ?? false,
            };
            return Results.Ok(await queries.ListAsync(query, ct));
        }).RequirePermission(Permissions.WorkOrder.View);

        wo.MapGet("/creation-options", async (Guid poId, WoQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetCreationOptionsAsync(poId, ct))).RequirePermission(Permissions.WorkOrder.Create);

        wo.MapPost("", async (SaveWoRequest request, CreateWorkOrderHandler handler, CancellationToken ct) =>
        {
            var created = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/work-orders/{created.Id}", created);
        }).RequirePermission(Permissions.WorkOrder.Create);

        wo.MapGet("/{id:guid}", async (Guid id, WoQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetDetailAsync(id, ct))).RequirePermission(Permissions.WorkOrder.View);

        wo.MapPut("/{id:guid}", async (Guid id, SaveWoRequest request, UpdateDraftWorkOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Edit);

        wo.MapPut("/{id:guid}/quantities", async (Guid id, UpdateWoQuantitiesRequest request, UpdateWoQuantitiesHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Edit);

        wo.MapPut("/{id:guid}/schedule", async (Guid id, UpdateWoScheduleRequest request, UpdateWoScheduleHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Edit);

        wo.MapPost("/{id:guid}/release/preview", async (Guid id, WoQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.PreviewReleaseAsync(id, ct))).RequirePermission(Permissions.WorkOrder.Release);

        wo.MapPost("/{id:guid}/release", async (Guid id, WoActionRequest request, ReleaseWorkOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Release);

        wo.MapPost("/{id:guid}/complete", async (Guid id, WoActionRequest request, CompleteWorkOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Complete);

        wo.MapPost("/{id:guid}/resume", async (Guid id, WoActionRequest request, ResumeWorkOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Complete);

        wo.MapPost("/{id:guid}/close/preview", async (Guid id, WoQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.PreviewCloseAsync(id, ct))).RequirePermission(Permissions.WorkOrder.Close);

        wo.MapPost("/{id:guid}/close", async (Guid id, WoActionRequest request, CloseWorkOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Close);

        wo.MapPost("/{id:guid}/reopen", async (Guid id, WoActionRequest request, ReopenWorkOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Reopen);

        wo.MapPost("/{id:guid}/cancel", async (Guid id, WoActionRequest request, CancelWorkOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.WorkOrder.Cancel);

        wo.MapGet("/{id:guid}/progress", async (Guid id, WoQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetProgressAsync(id, ct))).RequirePermission(Permissions.WorkOrder.View);

        wo.MapGet("/{id:guid}/tariff-snapshot", async (Guid id, WoQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetTariffSnapshotAsync(id, ct))).RequirePermission(Permissions.WorkOrder.Tarif.View);

        wo.MapGet("/{id:guid}/status-history", async (Guid id, WoQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetStatusHistoryAsync(id, ct))).RequirePermission(Permissions.WorkOrder.View);
    }
}
