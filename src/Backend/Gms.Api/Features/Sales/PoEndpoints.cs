using Gms.Api.Security;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Security;

namespace Gms.Api.Features.Sales;

public static class PoEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var po = app.MapGroup("/api/v1/purchase-orders").WithTags("Customer PO");

        po.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, PoStatus? status, Guid? customerId, DateOnly? from, DateOnly? to,
            bool? terlambat, bool? jatuhTempo7, bool? poCustomerKosong, bool? sisaRencana,
            ListPurchaseOrdersHandler handler, CancellationToken ct) =>
        {
            var query = new PoListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort,
                Status = status, CustomerId = customerId, From = from, To = to,
                Terlambat = terlambat ?? false, JatuhTempo7 = jatuhTempo7 ?? false,
                PoCustomerKosong = poCustomerKosong ?? false, SisaRencana = sisaRencana ?? false,
            };
            return Results.Ok(await handler.HandleAsync(query, ct));
        }).RequirePermission(Permissions.CustomerPO.View);

        po.MapGet("/{id:guid}", async (Guid id, GetPurchaseOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, ct))).RequirePermission(Permissions.CustomerPO.View);

        po.MapPost("", async (SavePoRequest request, CreatePurchaseOrderHandler handler, CancellationToken ct) =>
        {
            var created = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/purchase-orders/{created.Id}", created);
        }).RequirePermission(Permissions.CustomerPO.Create);

        po.MapPut("/{id:guid}", async (Guid id, SavePoRequest request, UpdatePurchaseOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.CustomerPO.Edit);

        po.MapPut("/{id:guid}/customer-po-number", async (Guid id, SetCustomerPoNumberRequest request, SetCustomerPoNumberHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.CustomerPO.Edit);

        po.MapPost("/{id:guid}/confirm", async (Guid id, uint rowVersion, ConfirmPurchaseOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, rowVersion, ct))).RequirePermission(Permissions.CustomerPO.Confirm);

        po.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest request, CancelPurchaseOrderHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, request, ct))).RequirePermission(Permissions.CustomerPO.Cancel);

        po.MapGet("/{id:guid}/allocations", async (Guid id, GetPoAllocationsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, ct))).RequirePermission(Permissions.CustomerPO.Alokasi.View);

        var lines = app.MapGroup("/api/v1/po-lines").WithTags("Customer PO - Alokasi");

        lines.MapGet("/{lineId:guid}/stock-availability", async (Guid lineId, GetStockAvailabilityHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(lineId, ct))).RequirePermission(Permissions.CustomerPO.Alokasi.View);

        lines.MapPost("/{lineId:guid}/allocate", async (Guid lineId, AllocateRequest request, AllocateStockHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(lineId, request, ct))).RequirePermission(Permissions.CustomerPO.Alokasi.Allocate);

        lines.MapPost("/{lineId:guid}/release-allocation", async (Guid lineId, ReleaseAllocationRequest request, ReleaseAllocationHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(lineId, request, ct))).RequirePermission(Permissions.CustomerPO.Alokasi.Release);
    }
}
