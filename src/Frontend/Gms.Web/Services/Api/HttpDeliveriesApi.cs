using Gms.Shared.Common;
using Gms.Shared.Deliveries;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IDeliveriesApi"/> ke API sungguhan (<c>/api/v1/deliveries</c>).</summary>
public sealed class HttpDeliveriesApi(GmsApiClient client) : IDeliveriesApi
{
    private const string B = "/api/v1/deliveries";

    public Task<IReadOnlyList<EligiblePoItem>> ListEligiblePosAsync(string? q, CancellationToken ct = default) =>
        client.GetAsync<IReadOnlyList<EligiblePoItem>>($"{B}/eligible-pos" + ApiQuery.Build(("q", q)), ct);

    public Task<DeliveryContextDto> GetContextAsync(Guid poId, CancellationToken ct = default) => client.GetAsync<DeliveryContextDto>($"{B}/context?poId={poId}", ct);

    public Task<PagedResult<DeliveryListItem>> ListAsync(DeliveryListQuery q, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<DeliveryListItem>>(B + ApiQuery.Build(("page", q.Page), ("pageSize", q.PageSize), ("q", q.Q), ("sort", q.Sort), ("poId", q.PoId), ("customerId", q.CustomerId), ("from", q.From), ("to", q.To), ("status", q.Status)), ct);

    public Task<DeliveryDto> GetAsync(Guid id, CancellationToken ct = default) => client.GetAsync<DeliveryDto>($"{B}/{id}", ct);

    public Task<DeliveryDto> CreateAsync(SaveDeliveryRequest request, CancellationToken ct = default) => client.PostAsync<DeliveryDto>(B, request, ct);

    public Task<DeliveryDto> UpdateInfoAsync(Guid id, UpdateDeliveryInfoRequest request, CancellationToken ct = default) => client.PutAsync<DeliveryDto>($"{B}/{id}/info", request, ct);

    public Task<ImpactPreview> PreviewVoidAsync(Guid id, CancellationToken ct = default) => client.PostAsync<ImpactPreview>($"{B}/{id}/void/preview", null, ct);

    public Task<DeliveryDto> VoidAsync(Guid id, ReasonRequest request, CancellationToken ct = default) => client.PostAsync<DeliveryDto>($"{B}/{id}/void", request, ct);

    public Task<ImpactPreview> PreviewCorrectAsync(Guid id, CorrectDeliveryRequest request, CancellationToken ct = default) => client.PostAsync<ImpactPreview>($"{B}/{id}/correct/preview", request, ct);

    public Task<DeliveryDto> CorrectAsync(Guid id, CorrectDeliveryRequest request, CancellationToken ct = default) => client.PostAsync<DeliveryDto>($"{B}/{id}/correct", request, ct);

    public Task<FileContent> GetPdfAsync(Guid id, CancellationToken ct = default) => client.DownloadAsync($"{B}/{id}/pdf", ct);
}
