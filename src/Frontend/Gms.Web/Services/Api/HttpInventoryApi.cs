using Gms.Shared.Common;
using Gms.Shared.Inventory;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IInventoryApi"/> ke API sungguhan (<c>/api/v1/handovers</c>, <c>/api/v1/stock</c>).</summary>
public sealed class HttpInventoryApi(GmsApiClient client) : IInventoryApi
{
    private sealed record IdResponse(Guid Id);

    private const string H = "/api/v1/handovers";

    public Task<HandoverFgContextDto> GetHandoverFgContextAsync(Guid woId, CancellationToken ct = default) => client.GetAsync<HandoverFgContextDto>($"{H}/fg/context?woId={woId}", ct);

    public Task<HandoverFgPreviewDto> PreviewHandoverFgAsync(SaveHandoverFgRequest request, CancellationToken ct = default) => client.PostAsync<HandoverFgPreviewDto>($"{H}/fg/preview", request, ct);

    public Task<HandoverFgResultDto> CreateHandoverFgAsync(SaveHandoverFgRequest request, CancellationToken ct = default) => client.PostAsync<HandoverFgResultDto>($"{H}/fg", request, ct);

    public Task<PagedResult<HandoverFgListItem>> ListHandoverFgAsync(HandoverFgListQuery q, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<HandoverFgListItem>>($"{H}/fg" + ApiQuery.Build(("page", q.Page), ("pageSize", q.PageSize), ("q", q.Q), ("woId", q.WoId), ("poId", q.PoId), ("fgId", q.FgId), ("from", q.From), ("to", q.To), ("status", q.Status)), ct);

    public Task<HandoverFgDto> GetHandoverFgAsync(Guid id, CancellationToken ct = default) => client.GetAsync<HandoverFgDto>($"{H}/fg/{id}", ct);

    public Task<ImpactPreview> PreviewVoidHandoverFgAsync(Guid id, CancellationToken ct = default) => client.PostAsync<ImpactPreview>($"{H}/fg/{id}/void/preview", null, ct);

    public Task VoidHandoverFgAsync(Guid id, ReasonRequest request, CancellationToken ct = default) => client.PostAsync($"{H}/fg/{id}/void", request, ct);

    public Task<ImpactPreview> PreviewCorrectHandoverFgAsync(Guid id, CorrectHandoverFgRequest request, CancellationToken ct = default) =>
        client.PostAsync<ImpactPreview>($"{H}/fg/{id}/correct/preview", request, ct);

    public Task<HandoverFgResultDto> CorrectHandoverFgAsync(Guid id, CorrectHandoverFgRequest request, CancellationToken ct = default) =>
        client.PostAsync<HandoverFgResultDto>($"{H}/fg/{id}/correct", request, ct);

    public async Task<Guid> CreateMaterialHandoverAsync(SaveMaterialHandoverRequest request, CancellationToken ct = default) =>
        (await client.PostAsync<IdResponse>($"{H}/material", request, ct)).Id;

    public Task<PagedResult<MaterialHandoverListItem>> ListMaterialHandoversAsync(MaterialHandoverListQuery q, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<MaterialHandoverListItem>>($"{H}/material" + ApiQuery.Build(("page", q.Page), ("pageSize", q.PageSize), ("q", q.Q), ("woId", q.WoId), ("from", q.From), ("to", q.To), ("status", q.Status)), ct);

    public Task<MaterialHandoverDto> GetMaterialHandoverAsync(Guid id, CancellationToken ct = default) => client.GetAsync<MaterialHandoverDto>($"{H}/material/{id}", ct);

    public Task VoidMaterialHandoverAsync(Guid id, ReasonRequest request, CancellationToken ct = default) => client.PostAsync($"{H}/material/{id}/void", request, ct);

    public Task<StockListResult> ListStockAsync(StockQuery q, CancellationToken ct = default) =>
        client.GetAsync<StockListResult>("/api/v1/stock" + ApiQuery.Build(("page", q.Page), ("pageSize", q.PageSize), ("q", q.Q), ("fgId", q.FgId), ("colorId", q.ColorId), ("sizeId", q.SizeId), ("customerId", q.CustomerId), ("hanyaAdaStok", q.HanyaAdaStok)), ct);

    public Task<StockLedgerResult> GetStockLedgerAsync(StockLedgerQuery q, CancellationToken ct = default) =>
        client.GetAsync<StockLedgerResult>("/api/v1/stock/ledger" + ApiQuery.Build(("page", q.Page), ("pageSize", q.PageSize), ("fgId", q.FgId), ("colorId", q.ColorId), ("sizeId", q.SizeId), ("from", q.From), ("to", q.To)), ct);

    public Task<ImpactPreview> PreviewAdjustStockAsync(AdjustStockRequest request, CancellationToken ct = default) => client.PostAsync<ImpactPreview>("/api/v1/stock/adjustments/preview", request, ct);

    public Task AdjustStockAsync(AdjustStockRequest request, CancellationToken ct = default) => client.PostAsync("/api/v1/stock/adjustments", request, ct);
}
