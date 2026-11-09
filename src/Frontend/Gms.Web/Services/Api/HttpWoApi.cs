using Gms.Shared.Common;
using Gms.Shared.WorkOrders;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IWorkOrdersApi"/> ke API sungguhan (<c>/api/v1/work-orders</c>).</summary>
public sealed class HttpWoApi(GmsApiClient client) : IWorkOrdersApi
{
    private static string B(Guid id) => $"/api/v1/work-orders/{id}";

    public Task<PagedResult<WoListItem>> ListAsync(WoListQuery q, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<WoListItem>>("/api/v1/work-orders" + ApiQuery.Build(
            ("page", q.Page), ("pageSize", q.PageSize), ("q", q.Q), ("sort", q.Sort), ("status", q.Status), ("poId", q.PoId), ("customerId", q.CustomerId),
            ("fgId", q.FgId), ("from", q.From), ("to", q.To), ("terlambat", q.Terlambat ? true : null), ("menungguClose", q.MenungguClose ? true : null)), ct);

    public Task<WoCreationOptionsDto> GetCreationOptionsAsync(Guid poId, CancellationToken ct = default) =>
        client.GetAsync<WoCreationOptionsDto>($"/api/v1/work-orders/creation-options?poId={poId}", ct);

    public Task<WoDto> GetAsync(Guid id, CancellationToken ct = default) => client.GetAsync<WoDto>(B(id), ct);

    public Task<WoDto> CreateAsync(SaveWoRequest request, CancellationToken ct = default) => client.PostAsync<WoDto>("/api/v1/work-orders", request, ct);

    public Task<WoDto> UpdateDraftAsync(Guid id, SaveWoRequest request, CancellationToken ct = default) => client.PutAsync<WoDto>(B(id), request, ct);

    public Task<WoDto> UpdateQuantitiesAsync(Guid id, UpdateWoQuantitiesRequest request, CancellationToken ct = default) => client.PutAsync<WoDto>($"{B(id)}/quantities", request, ct);

    public Task<WoDto> UpdateScheduleAsync(Guid id, UpdateWoScheduleRequest request, CancellationToken ct = default) => client.PutAsync<WoDto>($"{B(id)}/schedule", request, ct);

    public Task<WoReleasePreviewDto> PreviewReleaseAsync(Guid id, CancellationToken ct = default) => client.PostAsync<WoReleasePreviewDto>($"{B(id)}/release/preview", null, ct);

    public Task<WoDto> ReleaseAsync(Guid id, WoActionRequest request, CancellationToken ct = default) => client.PostAsync<WoDto>($"{B(id)}/release", request, ct);

    public Task<WoDto> CompleteAsync(Guid id, WoActionRequest request, CancellationToken ct = default) => client.PostAsync<WoDto>($"{B(id)}/complete", request, ct);

    public Task<WoDto> ResumeAsync(Guid id, WoActionRequest request, CancellationToken ct = default) => client.PostAsync<WoDto>($"{B(id)}/resume", request, ct);

    public Task<WoClosePreviewDto> PreviewCloseAsync(Guid id, CancellationToken ct = default) => client.PostAsync<WoClosePreviewDto>($"{B(id)}/close/preview", null, ct);

    public Task<WoDto> CloseAsync(Guid id, WoActionRequest request, CancellationToken ct = default) => client.PostAsync<WoDto>($"{B(id)}/close", request, ct);

    public Task<WoDto> ReopenAsync(Guid id, WoActionRequest request, CancellationToken ct = default) => client.PostAsync<WoDto>($"{B(id)}/reopen", request, ct);

    public Task<WoDto> CancelAsync(Guid id, WoActionRequest request, CancellationToken ct = default) => client.PostAsync<WoDto>($"{B(id)}/cancel", request, ct);

    public Task<WoProgressDto> GetProgressAsync(Guid id, CancellationToken ct = default) => client.GetAsync<WoProgressDto>($"{B(id)}/progress", ct);

    public Task<WoTariffSnapshotDto> GetTariffSnapshotAsync(Guid id, CancellationToken ct = default) => client.GetAsync<WoTariffSnapshotDto>($"{B(id)}/tariff-snapshot", ct);

    public Task<IReadOnlyList<WoStatusHistoryItem>> GetStatusHistoryAsync(Guid id, CancellationToken ct = default) =>
        client.GetAsync<IReadOnlyList<WoStatusHistoryItem>>($"{B(id)}/status-history", ct);
}
