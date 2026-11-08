using Gms.Domain.Sales;
using Gms.Shared.Enums;
using Gms.Shared.Master;

namespace Gms.Api.Tests.Infrastructure;

/// <summary>Helper pembuat data master lewat API (token Administrator bootstrap). Kode selalu unik per panggilan.</summary>
public static class MasterHelpers
{
    private const string Base = "/api/v1/master";

    public static string Code(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<T> PostAsync<T>(TestHost h, string route, object body) =>
        (await h.Api.PostAsync($"{Base}/{route}", body, h.AdminToken)).Ok($"POST {route}").As<T>();

    public static Task<SizeDto> NewSizeAsync(this TestHost h, string? code = null, int sortOrder = 10) =>
        PostAsync<SizeDto>(h, "sizes", new { code = code ?? Code("Z"), name = "Size uji", sortOrder });

    public static Task<ColorDto> NewColorAsync(this TestHost h, string? code = null) =>
        PostAsync<ColorDto>(h, "colors", new { code = code ?? Code("C"), name = "Color uji", hex = "#112233" });

    public static Task<SimpleMasterDto> NewUomAsync(this TestHost h, string? code = null) =>
        PostAsync<SimpleMasterDto>(h, "uoms", new { code = code ?? Code("U"), name = "Satuan uji" });

    public static Task<SimpleMasterDto> NewCategoryAsync(this TestHost h, string? code = null) =>
        PostAsync<SimpleMasterDto>(h, "material-categories", new { code = code ?? Code("K"), name = "Kategori uji" });

    public static async Task<MaterialDto> NewMaterialAsync(this TestHost h, string? code = null)
    {
        var category = await h.NewCategoryAsync();
        var uom = await h.NewUomAsync();
        return await PostAsync<MaterialDto>(h, "materials",
            new { code = code ?? Code("M"), name = "Material uji", categoryId = category.Id, uomId = uom.Id, description = "uji" });
    }

    public static Task<CustomerDto> NewCustomerAsync(this TestHost h, string? name = null) =>
        PostAsync<CustomerDto>(h, "customers", new { name = name ?? "Customer " + Code("") });

    public static Task<ProcessDto> NewProcessAsync(this TestHost h, ProcessType type = ProcessType.Internal, string? code = null, string? name = null) =>
        PostAsync<ProcessDto>(h, "processes", new { code = code ?? Code("P"), name = name ?? "Proses " + Code(""), type });

    public static Task<WorkerDto> NewWorkerAsync(this TestHost h, string? name = null, Guid? mainProcessId = null) =>
        PostAsync<WorkerDto>(h, "workers", new { name = name ?? "Pekerja " + Code(""), mainProcessId });

    public static Task<FgDto> NewFgAsync(this TestHost h, string? code = null, Guid? customerId = null) =>
        PostAsync<FgDto>(h, "fgs", new { code = code ?? Code("F"), name = "FG uji", kind = "Kemeja", customerId });

    // ===== Struktur FG =====

    public static async Task<uint> FgRowVersionAsync(this TestHost h, Guid fgId) =>
        (await h.Api.GetAsync($"{Base}/fgs/{fgId}/sizes", h.AdminToken)).Ok().As<FgSizesDto>().RowVersion;

    public static async Task<ApiResponse> PutSizesAsync(this TestHost h, Guid fgId, IEnumerable<Guid> sizeIds, bool confirm = false, uint? rowVersion = null) =>
        await h.Api.PutAsync($"{Base}/fgs/{fgId}/sizes",
            new SaveFgSizesRequest(sizeIds.ToList(), confirm, rowVersion ?? await h.FgRowVersionAsync(fgId)), h.AdminToken);

    public static async Task<ApiResponse> PutRoutingAsync(this TestHost h, Guid fgId, IEnumerable<Guid> processIds, bool confirm = false, uint? rowVersion = null) =>
        await h.Api.PutAsync($"{Base}/fgs/{fgId}/routing",
            new SaveRoutingRequest(processIds.ToList(), confirm, rowVersion ?? await h.FgRowVersionAsync(fgId)), h.AdminToken);

    public static async Task<ApiResponse> PutTariffsAsync(this TestHost h, Guid fgId, IEnumerable<TariffCell> cells, uint? rowVersion = null) =>
        await h.Api.PutAsync($"{Base}/fgs/{fgId}/tariffs",
            new SaveTariffsRequest(cells.ToList(), rowVersion ?? await h.FgRowVersionAsync(fgId)), h.AdminToken);

    public static async Task<FgTariffGridDto> GetGridAsync(this TestHost h, Guid fgId) =>
        (await h.Api.GetAsync($"{Base}/fgs/{fgId}/tariffs", h.AdminToken)).Ok().As<FgTariffGridDto>();

    public static async Task<FgReadinessDto> GetReadinessAsync(this TestHost h, Guid fgId) =>
        (await h.Api.GetAsync($"{Base}/fgs/{fgId}/readiness", h.AdminToken)).Ok().As<FgReadinessDto>();

    public static async Task<FgDto> GetFgAsync(this TestHost h, Guid fgId) =>
        (await h.Api.GetAsync($"{Base}/fgs/{fgId}", h.AdminToken)).Ok().As<FgDto>();

    /// <summary>Tarif penuh untuk seluruh sel grid (rate dasar + indeks) agar FG menjadi Siap Produksi.</summary>
    public static async Task FillAllTariffsAsync(this TestHost h, Guid fgId, decimal baseRate = 1000m)
    {
        var grid = await h.GetGridAsync(fgId);
        var cells = grid.InternalSteps
            .SelectMany((step, i) => grid.Sizes.Select((size, j) => new TariffCell(step.ProcessId, size.Id, baseRate + (i * 100) + j)))
            .ToList();
        (await h.PutTariffsAsync(fgId, cells)).Ok("isi semua tarif");
    }

    /// <summary>Hasil penyiapan FG lengkap: 2 size (S,M), routing CUT(Internal) → EMB(Vendor) → SEW(Internal), tarif terisi.</summary>
    public sealed record ReadyFg(FgDto Fg, SizeDto Small, SizeDto Medium, ProcessDto Cut, ProcessDto Emb, ProcessDto Sew);

    public static async Task<ReadyFg> NewReadyFgAsync(this TestHost h, Guid? customerId = null, bool fillTariffs = true)
    {
        var small = await h.NewSizeAsync(sortOrder: 10);
        var medium = await h.NewSizeAsync(sortOrder: 20);
        var cut = await h.NewProcessAsync(ProcessType.Internal, name: "Cutting");
        var emb = await h.NewProcessAsync(ProcessType.Vendor, name: "Bordir");
        var sew = await h.NewProcessAsync(ProcessType.Internal, name: "Sewing");
        var fg = await h.NewFgAsync(customerId: customerId);
        (await h.PutSizesAsync(fg.Id, [small.Id, medium.Id])).Ok("sizes");
        (await h.PutRoutingAsync(fg.Id, [cut.Id, emb.Id, sew.Id])).Ok("routing");
        if (fillTariffs)
        {
            await h.FillAllTariffsAsync(fg.Id);
        }

        return new ReadyFg(await h.GetFgAsync(fg.Id), small, medium, cut, emb, sew);
    }

    /// <summary>Menyisipkan PO + satu baris langsung ke database (modul PO belum ada) untuk menguji aturan pemakaian master.</summary>
    public static async Task<Guid> InsertPoLineAsync(this TestHost h, Guid fgId, Guid sizeId, PoStatus status = PoStatus.Confirmed)
    {
        var customer = await h.NewCustomerAsync();
        var color = await h.NewColorAsync();
        await using var db = h.CreateDbContext();
        var po = new PurchaseOrder
        {
            Code = Code("PO-T-"), CustomerId = customer.Id, PoDate = new DateOnly(2026, 11, 8), Status = status, CreatedAt = DateTimeOffset.UtcNow,
        };
        po.Lines.Add(new PoLine
        {
            FgId = fgId, ColorId = color.Id, SizeId = sizeId, DeliveryDate = new DateOnly(2026, 11, 30), Qty = 10, CreatedAt = DateTimeOffset.UtcNow,
        });
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();
        return po.Id;
    }
}
