using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Pola CRUD generik ke <c>/api/v1/master/{route}</c> (ApiConventions 4.2): daftar, detail, buat, ubah, (non)aktifkan, lookup.</summary>
public abstract class HttpMasterCrud<TItem, TDetail, TSave>(GmsApiClient client, string route) : IMasterCrudApi<TItem, TDetail, TSave>
{
    private string Base => $"/api/v1/master/{route}";

    public Task<PagedResult<TItem>> ListAsync(MasterListQuery query, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<TItem>>(Base + ApiQuery.Build(
            ("page", query.Page), ("pageSize", query.PageSize), ("q", query.Q), ("sort", query.Sort),
            ("status", query.Status.ToString().ToLowerInvariant()), ("readyOnly", (query as FgListQuery)?.ReadyOnly == true ? true : null)), ct);

    public Task<TDetail> GetAsync(Guid id, CancellationToken ct = default) => client.GetAsync<TDetail>($"{Base}/{id}", ct);

    public Task<TDetail> CreateAsync(TSave request, CancellationToken ct = default) => client.PostAsync<TDetail>(Base, request, ct);

    public Task<TDetail> UpdateAsync(Guid id, TSave request, CancellationToken ct = default) => client.PutAsync<TDetail>($"{Base}/{id}", request, ct);

    public Task<TDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct = default) =>
        client.PostAsync<TDetail>($"{Base}/{id}/{(isActive ? "activate" : "deactivate")}?rowVersion={rowVersion}", null, ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(string? q, CancellationToken ct = default) =>
        client.GetAsync<IReadOnlyList<LookupItem>>($"{Base}/lookup" + ApiQuery.Build(("q", q)), ct);
}

public sealed class HttpUomApi(GmsApiClient client) : HttpMasterCrud<SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>(client, "uoms"), IUomApi;

public sealed class HttpMaterialCategoryApi(GmsApiClient client) : HttpMasterCrud<SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>(client, "material-categories"), IMaterialCategoryApi;

public sealed class HttpMaterialApi(GmsApiClient client) : HttpMasterCrud<MaterialDto, MaterialDto, SaveMaterialRequest>(client, "materials"), IMaterialApi;

public sealed class HttpSizeApi(GmsApiClient client) : HttpMasterCrud<SizeDto, SizeDto, SaveSizeRequest>(client, "sizes"), ISizeApi;

public sealed class HttpColorApi(GmsApiClient client) : HttpMasterCrud<ColorDto, ColorDto, SaveColorRequest>(client, "colors"), IColorApi;

public sealed class HttpCustomerApi(GmsApiClient client) : HttpMasterCrud<CustomerDto, CustomerDto, SaveCustomerRequest>(client, "customers"), ICustomerApi;

public sealed class HttpVendorApi(GmsApiClient client) : HttpMasterCrud<VendorDto, VendorDto, SaveVendorRequest>(client, "vendors"), IVendorApi;

public sealed class HttpProcessApi(GmsApiClient client) : HttpMasterCrud<ProcessDto, ProcessDto, SaveProcessRequest>(client, "processes"), IProcessApi;

public sealed class HttpWorkerApi(GmsApiClient client) : HttpMasterCrud<WorkerDto, WorkerDto, SaveWorkerRequest>(client, "workers"), IWorkerApi;
