using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IPurchaseOrdersApi"/> ke API sungguhan (<c>/api/v1/purchase-orders</c>, <c>/api/v1/po-lines</c>).</summary>
public sealed class HttpPoApi(GmsApiClient client) : IPurchaseOrdersApi
{
    public Task<PagedResult<PoListItem>> ListAsync(PoListQuery q, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<PoListItem>>("/api/v1/purchase-orders" + ApiQuery.Build(
            ("page", q.Page), ("pageSize", q.PageSize), ("q", q.Q), ("sort", q.Sort), ("status", q.Status), ("customerId", q.CustomerId),
            ("from", q.From), ("to", q.To), ("terlambat", q.Terlambat ? true : null), ("jatuhTempo7", q.JatuhTempo7 ? true : null),
            ("poCustomerKosong", q.PoCustomerKosong ? true : null), ("sisaRencana", q.SisaRencana ? true : null)), ct);

    public Task<PoDto> GetAsync(Guid id, CancellationToken ct = default) => client.GetAsync<PoDto>($"/api/v1/purchase-orders/{id}", ct);

    public Task<PoDto> CreateAsync(SavePoRequest request, CancellationToken ct = default) => client.PostAsync<PoDto>("/api/v1/purchase-orders", request, ct);

    public Task<PoDto> UpdateAsync(Guid id, SavePoRequest request, CancellationToken ct = default) => client.PutAsync<PoDto>($"/api/v1/purchase-orders/{id}", request, ct);

    public Task<PoDto> SetCustomerPoNumberAsync(Guid id, SetCustomerPoNumberRequest request, CancellationToken ct = default) =>
        client.PutAsync<PoDto>($"/api/v1/purchase-orders/{id}/customer-po-number", request, ct);

    public Task<PoDto> ConfirmAsync(Guid id, uint rowVersion, CancellationToken ct = default) =>
        client.PostAsync<PoDto>($"/api/v1/purchase-orders/{id}/confirm?rowVersion={rowVersion}", null, ct);

    public Task<PoDto> CancelAsync(Guid id, ReasonRequest request, CancellationToken ct = default) =>
        client.PostAsync<PoDto>($"/api/v1/purchase-orders/{id}/cancel", request, ct);

    public Task<IReadOnlyList<AllocationEntryDto>> GetAllocationsAsync(Guid id, CancellationToken ct = default) =>
        client.GetAsync<IReadOnlyList<AllocationEntryDto>>($"/api/v1/purchase-orders/{id}/allocations", ct);

    public Task<StockAvailabilityDto> GetStockAvailabilityAsync(Guid poLineId, CancellationToken ct = default) =>
        client.GetAsync<StockAvailabilityDto>($"/api/v1/po-lines/{poLineId}/stock-availability", ct);

    public Task<PoLineDto> AllocateAsync(Guid poLineId, AllocateRequest request, CancellationToken ct = default) =>
        client.PostAsync<PoLineDto>($"/api/v1/po-lines/{poLineId}/allocate", request, ct);

    public Task<PoLineDto> ReleaseAllocationAsync(Guid poLineId, ReleaseAllocationRequest request, CancellationToken ct = default) =>
        client.PostAsync<PoLineDto>($"/api/v1/po-lines/{poLineId}/release-allocation", request, ct);
}
