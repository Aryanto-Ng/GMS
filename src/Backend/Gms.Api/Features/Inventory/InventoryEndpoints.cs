using Gms.Api.Security;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace Gms.Api.Features.Inventory;

public static class InventoryEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var fg = app.MapGroup("/api/v1/handovers/fg").WithTags("Serah Terima FG");

        fg.MapGet("/context", async (Guid woId, GetHandoverFgContextHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(woId, ct))).RequirePermission(Permissions.SerahTerimaFG.Create);

        fg.MapPost("/preview", async (SaveHandoverFgRequest request, CreateHandoverFgHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(request, ct))).RequirePermission(Permissions.SerahTerimaFG.Create);

        fg.MapPost("", async (SaveHandoverFgRequest request, CreateHandoverFgHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/handovers/fg/{result.Id}", result);
        }).RequirePermission(Permissions.SerahTerimaFG.Create);

        fg.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, Guid? woId, Guid? poId, Guid? fgId, DateOnly? from, DateOnly? to,
            DocStatus? status, HandoverFgQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListAsync(new HandoverFgListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort, WoId = woId, PoId = poId, FgId = fgId, From = from, To = to, Status = status,
            }, ct))).RequirePermission(Permissions.SerahTerimaFG.View);

        fg.MapGet("/{id:guid}", async (Guid id, HandoverFgQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAsync(id, ct))).RequirePermission(Permissions.SerahTerimaFG.View);

        fg.MapPost("/{id:guid}/void/preview", async (Guid id, VoidHandoverFgHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, ct))).RequirePermission(Permissions.SerahTerimaFG.Void);

        fg.MapPost("/{id:guid}/void", async (Guid id, ReasonRequest request, VoidHandoverFgHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(id, request, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SerahTerimaFG.Void);

        fg.MapPost("/{id:guid}/correct/preview", async (Guid id, CorrectHandoverFgRequest request, CorrectHandoverFgHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(id, request, ct))).RequirePermission(Permissions.SerahTerimaFG.Correct);

        fg.MapPost("/{id:guid}/correct", async (Guid id, CorrectHandoverFgRequest request, CorrectHandoverFgHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.SerahTerimaFG.Correct);

        var material = app.MapGroup("/api/v1/handovers/material").WithTags("Serah Terima Material");

        material.MapPost("", async (SaveMaterialHandoverRequest request, CreateMaterialHandoverHandler handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/handovers/material/{id}", new { id });
        }).RequirePermission(Permissions.SerahTerimaMaterial.Create);

        material.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, Guid? woId, DateOnly? from, DateOnly? to, DocStatus? status,
            MaterialHandoverQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListAsync(new MaterialHandoverListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort, WoId = woId, From = from, To = to, Status = status,
            }, ct))).RequirePermission(Permissions.SerahTerimaMaterial.View);

        material.MapGet("/{id:guid}", async (Guid id, MaterialHandoverQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetAsync(id, ct))).RequirePermission(Permissions.SerahTerimaMaterial.View);

        material.MapPost("/{id:guid}/void", async (Guid id, ReasonRequest request, VoidMaterialHandoverHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(id, request, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SerahTerimaMaterial.Void);

        var stock = app.MapGroup("/api/v1/stock").WithTags("Stok FG");

        stock.MapGet("", async (
            int? page, int? pageSize, string? q, Guid? fgId, Guid? colorId, Guid? sizeId, Guid? customerId, bool? hanyaAdaStok,
            StockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.ListAsync(new StockQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, FgId = fgId, ColorId = colorId, SizeId = sizeId, CustomerId = customerId,
                HanyaAdaStok = hanyaAdaStok ?? true,
            }, ct))).RequirePermission(Permissions.StokFG.View);

        stock.MapGet("/ledger", async (
            Guid fgId, Guid colorId, Guid sizeId, DateOnly? from, DateOnly? to, int? page, int? pageSize, StockQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.GetLedgerAsync(new StockLedgerQuery
            {
                FgId = fgId, ColorId = colorId, SizeId = sizeId, From = from, To = to, Page = page ?? 1, PageSize = pageSize ?? 50,
            }, ct))).RequirePermission(Permissions.StokFG.View);

        stock.MapPost("/adjustments/preview", async (AdjustStockRequest request, AdjustStockHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.PreviewAsync(request, ct))).RequirePermission(Permissions.StokFG.Adjust);

        stock.MapPost("/adjustments", async (AdjustStockRequest request, AdjustStockHandler handler, CancellationToken ct) =>
        {
            await handler.HandleAsync(request, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.StokFG.Adjust);

        stock.MapGet("/opening-balance/import-template", ([FromServices] StockOpeningImportHandler handler) =>
        {
            var file = handler.GetTemplate();
            return Results.File(file.Bytes, file.ContentType, file.FileName);
        }).RequirePermission(Permissions.StokFG.Import);

        stock.MapPost("/opening-balance/import", async (
                IFormFile file, bool? dryRun, [FromServices] StockOpeningImportHandler handler, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return Results.Ok(await handler.ImportAsync(stream, dryRun ?? true, ct));
            })
            .DisableAntiforgery()
            .RequirePermission(Permissions.StokFG.Import);
    }
}
