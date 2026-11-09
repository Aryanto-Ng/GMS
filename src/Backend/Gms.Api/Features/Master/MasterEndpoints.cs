using Gms.Api.Security;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace Gms.Api.Features.Master;

public static class MasterEndpoints
{
    private const string Base = "/api/v1/master";

    public static void Map(IEndpointRouteBuilder app)
    {
        MapCrud<UomHandler, SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>(app, "uoms",
            Permissions.MasterData.UoM.View, Permissions.MasterData.UoM.Create, Permissions.MasterData.UoM.Edit, Permissions.MasterData.UoM.Deactivate);
        MapCrud<MaterialCategoryHandler, SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>(app, "material-categories",
            Permissions.MasterData.KategoriMaterial.View, Permissions.MasterData.KategoriMaterial.Create,
            Permissions.MasterData.KategoriMaterial.Edit, Permissions.MasterData.KategoriMaterial.Deactivate);
        MapCrud<MaterialHandler, MaterialDto, MaterialDto, SaveMaterialRequest>(app, "materials",
            Permissions.MasterData.Material.View, Permissions.MasterData.Material.Create,
            Permissions.MasterData.Material.Edit, Permissions.MasterData.Material.Deactivate);
        MapCrud<SizeHandler, SizeDto, SizeDto, SaveSizeRequest>(app, "sizes",
            Permissions.MasterData.Size.View, Permissions.MasterData.Size.Create, Permissions.MasterData.Size.Edit, Permissions.MasterData.Size.Deactivate);
        MapCrud<ColorHandler, ColorDto, ColorDto, SaveColorRequest>(app, "colors",
            Permissions.MasterData.Color.View, Permissions.MasterData.Color.Create, Permissions.MasterData.Color.Edit, Permissions.MasterData.Color.Deactivate);
        MapCrud<CustomerHandler, CustomerDto, CustomerDto, SaveCustomerRequest>(app, "customers",
            Permissions.MasterData.Customer.View, Permissions.MasterData.Customer.Create,
            Permissions.MasterData.Customer.Edit, Permissions.MasterData.Customer.Deactivate);
        MapCrud<ProcessHandler, ProcessDto, ProcessDto, SaveProcessRequest>(app, "processes",
            Permissions.MasterData.Process.View, Permissions.MasterData.Process.Create,
            Permissions.MasterData.Process.Edit, Permissions.MasterData.Process.Deactivate);
        MapCrud<VendorHandler, VendorDto, VendorDto, SaveVendorRequest>(app, "vendors",
            Permissions.MasterData.Vendor.View, Permissions.MasterData.Vendor.Create,
            Permissions.MasterData.Vendor.Edit, Permissions.MasterData.Vendor.Deactivate);
        MapCrud<WorkerHandler, WorkerDto, WorkerDto, SaveWorkerRequest>(app, "workers",
            Permissions.MasterData.Pekerja.View, Permissions.MasterData.Pekerja.Create,
            Permissions.MasterData.Pekerja.Edit, Permissions.MasterData.Pekerja.Deactivate);
        MapCrud<FgHandler, FgListItem, FgDto, SaveFgRequest>(app, "fgs",
            Permissions.MasterData.FG.View, Permissions.MasterData.FG.Create, Permissions.MasterData.FG.Edit, Permissions.MasterData.FG.Deactivate);

        MapFgStructure(app);
        MapImport(app);
    }

    private static void MapCrud<THandler, TList, TDetail, TSave>(
        IEndpointRouteBuilder app, string route, string view, string create, string edit, string deactivate)
        where THandler : class, IMasterCrud<TList, TDetail, TSave>
    {
        var group = app.MapGroup($"{Base}/{route}").WithTags("Master Data");

        // Handler diambil dari RequestServices (bukan parameter lambda generik) karena RouteHandlerAnalyzer
        // ASP.NET Core mogok pada parameter bertipe generik.
        group.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, string? status, bool? readyOnly,
            HttpContext http, CancellationToken ct) =>
        {
            // Konvensi API (§4.2): status huruf kecil (active|inactive|all). Binding enum bawaan peka huruf besar/kecil (BUG-001),
            // maka di-parse manual tanpa membedakan huruf.
            ActiveFilter filter = ActiveFilter.Active;
            if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse(status, ignoreCase: true, out filter) | !Enum.IsDefined(filter))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Nilai status tidak valid.", detail: "Gunakan active, inactive, atau all.");
            }

            var handler = http.RequestServices.GetRequiredService<THandler>();
            var query = new FgListQuery
            {
                Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort,
                Status = filter, ReadyOnly = readyOnly ?? false,
            };
            return Results.Ok(await handler.ListAsync(query, ct));
        }).RequirePermission(view);

        group.MapGet("/lookup", async (string? q, HttpContext http, CancellationToken ct) =>
            Results.Ok(await http.RequestServices.GetRequiredService<THandler>().LookupAsync(q, ct))).RequirePermission(view);

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, CancellationToken ct) =>
            Results.Ok(await http.RequestServices.GetRequiredService<THandler>().GetAsync(id, ct))).RequirePermission(view);

        group.MapPost("", async (HttpContext http, CancellationToken ct) =>
        {
            var request = await ReadBodyAsync<TSave>(http, ct);
            var created = await http.RequestServices.GetRequiredService<THandler>().CreateAsync(request, ct);
            return Results.Created($"{Base}/{route}", created);
        }).RequirePermission(create);

        group.MapPut("/{id:guid}", async (Guid id, HttpContext http, CancellationToken ct) =>
        {
            var request = await ReadBodyAsync<TSave>(http, ct);
            return Results.Ok(await http.RequestServices.GetRequiredService<THandler>().UpdateAsync(id, request, ct));
        }).RequirePermission(edit);

        group.MapPost("/{id:guid}/deactivate", async (Guid id, uint rowVersion, HttpContext http, CancellationToken ct) =>
            Results.Ok(await http.RequestServices.GetRequiredService<THandler>().SetActiveAsync(id, false, rowVersion, ct)))
            .RequirePermission(deactivate);

        group.MapPost("/{id:guid}/activate", async (Guid id, uint rowVersion, HttpContext http, CancellationToken ct) =>
            Results.Ok(await http.RequestServices.GetRequiredService<THandler>().SetActiveAsync(id, true, rowVersion, ct)))
            .RequirePermission(deactivate);
    }

    /// <summary>Membaca isi JSON dengan opsi yang sama dengan API; isi rusak/kosong menjadi 400 bad-request.</summary>
    private static async Task<T> ReadBodyAsync<T>(HttpContext http, CancellationToken ct)
    {
        var body = await http.Request.ReadFromJsonAsync<T>(Common.JsonDefaults.Options, ct);
        return body ?? throw new BadHttpRequestException("Isi permintaan kosong.");
    }

    private static void MapFgStructure(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{Base}/fgs/{{id:guid}}").WithTags("Master Data - FG");

        group.MapGet("/sizes", async (Guid id, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.GetSizesAsync(id, ct))).RequirePermission(Permissions.MasterData.FG.View);
        group.MapPut("/sizes", async (Guid id, SaveFgSizesRequest request, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.SaveSizesAsync(id, request, ct))).RequirePermission(Permissions.MasterData.FG.Edit);

        group.MapGet("/routing", async (Guid id, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.GetRoutingAsync(id, ct))).RequirePermission(Permissions.MasterData.Routing.View);
        group.MapPut("/routing", async (Guid id, SaveRoutingRequest request, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.SaveRoutingAsync(id, request, ct))).RequirePermission(Permissions.MasterData.Routing.Edit);

        group.MapGet("/tariffs", async (Guid id, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.GetTariffsAsync(id, ct))).RequirePermission(Permissions.MasterData.Tarif.View);
        group.MapPut("/tariffs", async (Guid id, SaveTariffsRequest request, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.SaveTariffsAsync(id, request, ct))).RequirePermission(Permissions.MasterData.Tarif.Edit);

        group.MapGet("/readiness", async (Guid id, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.GetReadinessAsync(id, ct))).RequirePermission(Permissions.MasterData.FG.View);

        // Salin routing + tarif butuh kedua hak akses (AC-MD-27).
        group.MapPost("/copy-from/preview", async (Guid id, CopyFgRequest request, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.PreviewCopyAsync(id, request, ct)))
            .RequirePermission(Permissions.MasterData.Routing.Edit).RequirePermission(Permissions.MasterData.Tarif.Edit);
        group.MapPost("/copy-from", async (Guid id, CopyFgRequest request, [FromServices] FgStructureHandler h, CancellationToken ct) =>
            Results.Ok(await h.CopyFromAsync(id, request, ct)))
            .RequirePermission(Permissions.MasterData.Routing.Edit).RequirePermission(Permissions.MasterData.Tarif.Edit);
    }

    private static void MapImport(IEndpointRouteBuilder app)
    {
        MapImportFor(app, "materials", ImportEntity.Material, Permissions.MasterData.Material.Import);
        MapImportFor(app, "fgs", ImportEntity.Fg, Permissions.MasterData.FG.Import);
        MapImportFor(app, "workers", ImportEntity.Worker, Permissions.MasterData.Pekerja.Import);
    }

    private static void MapImportFor(IEndpointRouteBuilder app, string route, ImportEntity entity, string permission)
    {
        app.MapGet($"{Base}/{route}/import-template", ([FromServices] MasterImportHandler h) =>
        {
            var file = h.GetTemplate(entity);
            return Results.File(file.Bytes, file.ContentType, file.FileName);
        }).WithTags("Master Data - Impor").RequirePermission(permission);

        // Unggah multipart; dryRun bawaan true agar tidak sengaja menulis data. Otorisasi memakai bearer (bukan cookie).
        app.MapPost($"{Base}/{route}/import", async (
                IFormFile file, bool? dryRun, [FromServices] MasterImportHandler h, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return Results.Ok(await h.ImportAsync(entity, stream, file.FileName, dryRun ?? true, ct));
            })
            .WithTags("Master Data - Impor")
            .DisableAntiforgery()
            .RequirePermission(permission);
    }
}
