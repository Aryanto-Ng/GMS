using Gms.Shared.Common;
using Gms.Shared.Production;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IProductionApi"/> ke API sungguhan (<c>/api/v1/production</c>).</summary>
public sealed class HttpProductionApi(GmsApiClient client) : IProductionApi
{
    private sealed record IdResponse(Guid Id);

    private const string B = "/api/v1/production";

    public Task<InputContextDto> GetInputContextAsync(Guid woId, int seq, DateOnly workDate, CancellationToken ct = default) =>
        client.GetAsync<InputContextDto>($"{B}/input-context" + ApiQuery.Build(("woId", woId), ("seq", seq), ("workDate", workDate)), ct);

    public Task<SaveOutputsResult> SaveOutputsAsync(SaveOutputsRequest request, CancellationToken ct = default) => client.PostAsync<SaveOutputsResult>($"{B}/outputs", request, ct);

    public Task<PagedResult<EntryListItem>> ListEntriesAsync(EntryListQuery q, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<EntryListItem>>($"{B}/entries" + ApiQuery.Build(
            ("page", q.Page), ("pageSize", q.PageSize), ("q", q.Q), ("sort", q.Sort), ("woId", q.WoId), ("seq", q.Seq), ("workerId", q.WorkerId), ("vendorId", q.VendorId),
            ("from", q.From), ("to", q.To), ("kind", q.Kind), ("status", q.Status), ("hariIni", q.HariIni ? true : null), ("includeVoided", q.IncludeVoided ? true : null)), ct);

    public Task<EntryDetailDto> GetEntryAsync(Guid id, CancellationToken ct = default) => client.GetAsync<EntryDetailDto>($"{B}/entries/{id}", ct);

    public Task<ImpactPreview> PreviewVoidAsync(Guid id, CancellationToken ct = default) => client.PostAsync<ImpactPreview>($"{B}/entries/{id}/void/preview", null, ct);

    public Task VoidAsync(Guid id, ReasonRequest request, CancellationToken ct = default) => client.PostAsync($"{B}/entries/{id}/void", request, ct);

    public Task<ImpactPreview> PreviewCorrectOutputAsync(Guid id, CorrectOutputRequest request, CancellationToken ct = default) =>
        client.PostAsync<ImpactPreview>($"{B}/entries/{id}/correct/preview", request, ct);

    public async Task<Guid> CorrectOutputAsync(Guid id, CorrectOutputRequest request, CancellationToken ct = default) =>
        (await client.PostAsync<IdResponse>($"{B}/entries/{id}/correct", request, ct)).Id;

    public async Task<Guid> CreateAdjustmentAsync(AdjustmentRequest request, CancellationToken ct = default) =>
        (await client.PostAsync<IdResponse>($"{B}/adjustments", request, ct)).Id;

    public Task<VendorContextDto> GetVendorContextAsync(Guid woId, int seq, Guid? vendorId, CancellationToken ct = default) =>
        client.GetAsync<VendorContextDto>($"{B}/vendor-context" + ApiQuery.Build(("woId", woId), ("seq", seq), ("vendorId", vendorId)), ct);

    public async Task<Guid> SaveVendorMovementAsync(SaveVendorMovementRequest request, CancellationToken ct = default) =>
        (await client.PostAsync<IdResponse>($"{B}/vendor-movements", request, ct)).Id;

    public Task<ImpactPreview> PreviewVoidVendorMovementAsync(Guid id, CancellationToken ct = default) =>
        client.PostAsync<ImpactPreview>($"{B}/vendor-movements/{id}/void/preview", null, ct);

    public Task VoidVendorMovementAsync(Guid id, ReasonRequest request, CancellationToken ct = default) => client.PostAsync($"{B}/vendor-movements/{id}/void", request, ct);

    public Task<ImpactPreview> PreviewCorrectVendorMovementAsync(Guid id, CorrectVendorMovementRequest request, CancellationToken ct = default) =>
        client.PostAsync<ImpactPreview>($"{B}/vendor-movements/{id}/correct/preview", request, ct);

    public async Task<Guid> CorrectVendorMovementAsync(Guid id, CorrectVendorMovementRequest request, CancellationToken ct = default) =>
        (await client.PostAsync<IdResponse>($"{B}/vendor-movements/{id}/correct", request, ct)).Id;
}
