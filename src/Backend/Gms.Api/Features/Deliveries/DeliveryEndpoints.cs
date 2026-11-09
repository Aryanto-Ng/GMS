using Gms.Api.Security;
using Gms.Shared.Common;
using Gms.Shared.Deliveries;
using Gms.Shared.Enums;
using Gms.Shared.Security;

namespace Gms.Api.Features.Deliveries;

public static class DeliveryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/deliveries").WithTags("Pengiriman");

        g.MapGet("/eligible-pos", async (string? q, DeliveryContextHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.ListEligibleAsync(q, ct))).RequirePermission(Permissions.Pengiriman.Create);

        g.MapGet("/context", async (Guid poId, DeliveryContextHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.GetAsync(poId, ct))).RequirePermission(Permissions.Pengiriman.Create);

        g.MapPost("", async (SaveDeliveryRequest request, CreateDeliveryHandler handler, CancellationToken ct) =>
        {
            var created = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/deliveries/{created.Item.Id}", created);
        }).RequirePermission(Permissions.Pengiriman.Create);

        g.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, Guid? poId, Guid? customerId, DateOnly? from, DateOnly? to, DocStatus? status,
            DeliveryQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListAsync(new DeliveryListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort, PoId = poId, CustomerId = customerId, From = from, To = to, Status = status,
            }, ct))).RequirePermission(Permissions.Pengiriman.View);

        g.MapGet("/{id:guid}", async (Guid id, DeliveryQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAsync(id, ct))).RequirePermission(Permissions.Pengiriman.View);

        g.MapPut("/{id:guid}/info", async (Guid id, UpdateDeliveryInfoRequest request, UpdateDeliveryInfoHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.Pengiriman.Edit);

        g.MapPost("/{id:guid}/void/preview", async (Guid id, VoidDeliveryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, ct))).RequirePermission(Permissions.Pengiriman.Void);

        g.MapPost("/{id:guid}/void", async (Guid id, ReasonRequest request, VoidDeliveryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.Pengiriman.Void);

        g.MapPost("/{id:guid}/correct/preview", async (Guid id, CorrectDeliveryRequest request, CorrectDeliveryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, request, ct))).RequirePermission(Permissions.Pengiriman.Correct);

        g.MapPost("/{id:guid}/correct", async (Guid id, CorrectDeliveryRequest request, CorrectDeliveryHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.Pengiriman.Correct);
    }
}
