using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Deliveries;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Infrastructure;

public static class DlvHelpers
{
    private const string Base = "/api/v1/deliveries";

    /// <summary>PO terkonfirmasi (S dan M) dengan stok fisik dan alokasi siap kirim.</summary>
    public static async Task<(PoWorld World, PoDto Po)> NewDeliverableAsync(
        this TestHost h, int poSmall = 100, int poMedium = 50, int allocSmall = 40, int allocMedium = 20, int stockSmall = 60, int stockMedium = 30)
    {
        var w = await h.NewPoWorldAsync();
        await h.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Small.Id, stockSmall);
        await h.SetStockAsync(w.Fg.Fg.Id, w.Color.Id, w.Medium.Id, stockMedium);
        var po = await h.NewPoAsync(w, [w.Line(w.Small, poSmall), w.Line(w.Medium, poMedium)]);
        if (allocSmall > 0)
        {
            (await h.Api.PostAsync($"/api/v1/po-lines/{po.PoLineId(w.Small)}/allocate", new AllocateRequest(allocSmall, null), h.AdminToken)).Ok();
        }

        if (allocMedium > 0)
        {
            (await h.Api.PostAsync($"/api/v1/po-lines/{po.PoLineId(w.Medium)}/allocate", new AllocateRequest(allocMedium, null), h.AdminToken)).Ok();
        }

        return (w, po);
    }

    public static Task<ApiResponse> PostDeliveryAsync(this TestHost h, PoDto po, IEnumerable<(Guid Line, int Qty)> lines,
        DateOnly? date = null, string? recipient = "PT Penerima", string? token = null) =>
        h.Api.PostAsync(Base,
            new SaveDeliveryRequest(po.Id, date ?? Today(), recipient, "B 1234 XY", "Pak Sopir", "catatan", lines.Select(l => new DeliveryLineQty(l.Line, l.Qty)).ToList()),
            token ?? h.AdminToken);

    public static async Task<DeliveryDto> DeliverAsync(this TestHost h, PoDto po, params (Guid Line, int Qty)[] lines) =>
        (await h.PostDeliveryAsync(po, lines)).Ok("buat surat jalan").As<DeliveryDto>();

    public static async Task<DeliveryDto> GetDeliveryAsync(this TestHost h, Guid id, string? token = null) =>
        (await h.Api.GetAsync($"{Base}/{id}", token ?? h.AdminToken)).Ok().As<DeliveryDto>();

    public static Task<ApiResponse> VoidDeliveryAsync(this TestHost h, Guid id, string? reason = "salah kirim", string? token = null) =>
        h.Api.PostAsync($"{Base}/{id}/void", new ReasonRequest(reason ?? "", 0), token ?? h.AdminToken);

    public static Task<ApiResponse> CorrectDeliveryAsync(this TestHost h, Guid id, IEnumerable<(Guid Line, int Qty)> lines,
        string reason = "salah qty", string? recipient = "PT Baru", string? token = null, bool preview = false) =>
        h.Api.PostAsync($"{Base}/{id}/correct{(preview ? "/preview" : "")}",
            new CorrectDeliveryRequest(reason, Today(), recipient, "B 9 ZZ", "Sopir Baru", null, lines.Select(l => new DeliveryLineQty(l.Line, l.Qty)).ToList()),
            token ?? h.AdminToken);

    public static async Task<(int Allocated, int Delivered)> LineAsync(this TestHost h, Guid poLineId)
    {
        await using var db = h.CreateDbContext();
        var line = await db.PoLines.AsNoTracking().SingleAsync(l => l.Id == poLineId);
        return (line.AllocatedQty, line.DeliveredQty);
    }
}
