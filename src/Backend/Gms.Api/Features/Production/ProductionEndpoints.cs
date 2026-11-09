using Gms.Api.Security;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Production;
using Gms.Shared.Security;

namespace Gms.Api.Features.Production;

public static class ProductionEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/production").WithTags("Production Output");

        g.MapGet("/input-context", async (Guid woId, int seq, DateOnly workDate, GetInputContextHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(woId, seq, workDate, ct))).RequirePermission(Permissions.ProductionOutput.Create);

        g.MapPost("/outputs", async (SaveOutputsRequest request, SaveOutputsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(request, ct))).RequirePermission(Permissions.ProductionOutput.Create);

        g.MapGet("/entries", async (
            int? page, int? pageSize, string? q, string? sort, Guid? woId, int? seq, Guid? workerId, Guid? vendorId, DateOnly? from,
            DateOnly? to, EntryKind? kind, DocStatus? status, bool? hariIni, bool? includeVoided, EntryQueries queries, CancellationToken ct) =>
        {
            var query = new EntryListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort, WoId = woId, Seq = seq, WorkerId = workerId, VendorId = vendorId,
                From = from, To = to, Kind = kind, Status = status, HariIni = hariIni ?? false, IncludeVoided = includeVoided ?? false,
            };
            return Results.Ok(await queries.ListAsync(query, ct));
        }).RequireAnyPermission(Permissions.ProductionOutput.View, Permissions.VendorJob.View);

        g.MapGet("/entries/{id:guid}", async (Guid id, EntryQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAsync(id, ct))).RequireAnyPermission(Permissions.ProductionOutput.View, Permissions.VendorJob.View);

        g.MapPost("/entries/{id:guid}/void/preview", async (Guid id, VoidOutputHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, ct))).RequirePermission(Permissions.ProductionOutput.Void);

        g.MapPost("/entries/{id:guid}/void", async (Guid id, ReasonRequest request, VoidOutputHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(id, request, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ProductionOutput.Void);

        g.MapPost("/entries/{id:guid}/correct/preview", async (Guid id, CorrectOutputRequest request, CorrectOutputHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, request, ct))).RequirePermission(Permissions.ProductionOutput.Correct);

        g.MapPost("/entries/{id:guid}/correct", async (Guid id, CorrectOutputRequest request, CorrectOutputHandler handler, CancellationToken ct) =>
            Results.Ok(new { id = await handler.HandleAsync(id, request, ct) })).RequirePermission(Permissions.ProductionOutput.Correct);

        g.MapPost("/adjustments", async (AdjustmentRequest request, CreateAdjustmentHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/production/entries/{id}", new { id });
        }).RequirePermission(Permissions.ProductionOutput.Adjust);

        g.MapGet("/vendor-context", async (Guid woId, int seq, Guid? vendorId, GetVendorContextHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(woId, seq, vendorId, ct))).RequirePermission(Permissions.VendorJob.Create);

        g.MapPost("/vendor-movements", async (SaveVendorMovementRequest request, SaveVendorMovementHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/production/entries/{id}", new { id });
        }).RequirePermission(Permissions.VendorJob.Create);

        g.MapPost("/vendor-movements/{id:guid}/void/preview", async (Guid id, VoidVendorMovementHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, ct))).RequirePermission(Permissions.VendorJob.Void);

        g.MapPost("/vendor-movements/{id:guid}/void", async (Guid id, ReasonRequest request, VoidVendorMovementHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(id, request, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.VendorJob.Void);

        g.MapPost("/vendor-movements/{id:guid}/correct/preview", async (Guid id, CorrectVendorMovementRequest request, CorrectVendorMovementHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, request, ct))).RequirePermission(Permissions.VendorJob.Void);

        g.MapPost("/vendor-movements/{id:guid}/correct", async (Guid id, CorrectVendorMovementRequest request, CorrectVendorMovementHandler handler, CancellationToken ct) =>
            Results.Ok(new { id = await handler.HandleAsync(id, request, ct) })).RequirePermission(Permissions.VendorJob.Void);
    }
}
