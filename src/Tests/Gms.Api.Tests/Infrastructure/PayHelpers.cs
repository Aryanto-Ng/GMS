using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Master;
using Gms.Shared.Payroll;
using Gms.Shared.Production;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Infrastructure;

public static class PayHelpers
{
    private const string Base = "/api/v1/payroll/periods";

    /// <summary>WO Released dengan tarif dasar tertentu. Tarif: CUT-S = b, CUT-M = b+1, SEW-S = b+100, SEW-M = b+101.</summary>
    public static async Task<ProdSetup> NewRatedSetupAsync(this TestHost h, decimal baseRate = 1000m, int poQty = 500, int woQty = 400)
    {
        var w = await h.NewPoWorldAsync();
        await h.FillAllTariffsAsync(w.Fg.Fg.Id, baseRate);
        var po = await h.NewPoAsync(w, [w.Line(w.Small, poQty), w.Line(w.Medium, poQty)]);
        var wo = await h.NewWoAsync(w, po, (po.PoLineId(w.Small), woQty), (po.PoLineId(w.Medium), woQty));
        wo = await h.WoDoAsync(wo.Id, "release");
        var progress = (await h.Api.GetAsync($"/api/v1/work-orders/{wo.Id}/progress", h.AdminToken)).Ok().As<Gms.Shared.WorkOrders.WoProgressDto>();
        return new ProdSetup(w, po, wo, progress.Steps[0].Seq, progress.Steps[1].Seq, progress.Steps[2].Seq,
            wo.Lines.Single(l => l.ColorSize.SizeId == w.Small.Id).Id, wo.Lines.Single(l => l.ColorSize.SizeId == w.Medium.Id).Id);
    }

    /// <summary>Output CUT (langkah pertama, tanpa batas atas) pada tanggal tertentu.</summary>
    public static async Task<Guid> CutAsync(this TestHost h, ProdSetup s, Guid worker, DateOnly date, params (Guid Line, int Qty)[] cells)
    {
        var result = (await h.PostOutputsAsync(s, s.Cut, [Row(worker, cells)], date, confirm: true)).Ok("output CUT").As<SaveOutputsResult>();
        return result.EntryIds.Single();
    }

    public static Task<ApiResponse> CreatePeriodAsync(this TestHost h, DateOnly? start, DateOnly end, string? notes = null, string? token = null) =>
        h.Api.PostAsync(Base, new SavePeriodRequest(start, end, notes), token ?? h.AdminToken);

    public static async Task<PayrollPeriodDto> NewPeriodAsync(this TestHost h, DateOnly? start, DateOnly end) =>
        (await h.CreatePeriodAsync(start, end)).Ok("buat periode").As<PayrollPeriodDto>();

    public static async Task<PayrollPeriodDto> GetPeriodAsync(this TestHost h, Guid id, string? token = null) =>
        (await h.Api.GetAsync($"{Base}/{id}", token ?? h.AdminToken)).Ok().As<PayrollPeriodDto>();

    public static async Task<PayrollPeriodDto> ClosePeriodAsync(this TestHost h, Guid id) =>
        (await h.TryClosePeriodAsync(id)).Ok("tutup periode").As<PayrollPeriodDto>();

    public static async Task<ApiResponse> TryClosePeriodAsync(this TestHost h, Guid id, string? code = null, string? token = null)
    {
        var period = await h.GetPeriodAsync(id);
        return await h.Api.PostAsync($"{Base}/{id}/close", new ClosePeriodRequest(code ?? period.Code, period.RowVersion), token ?? h.AdminToken);
    }

    public static async Task<PayrollSummaryDto> SummaryAsync(this TestHost h, Guid id, string query = "", string? token = null) =>
        (await h.Api.GetAsync($"{Base}/{id}/summary{query}", token ?? h.AdminToken)).Ok().As<PayrollSummaryDto>();

    public static Task<ApiResponse> GetPayrollAsync(this TestHost h, Guid id, string path, string? token = null) =>
        h.Api.GetAsync($"{Base}/{id}/{path}", token ?? h.AdminToken);

    public static Task<(System.Net.HttpStatusCode Status, byte[] Bytes, string? ContentType, string? FileName)> DownloadPayrollAsync(
        this TestHost h, Guid id, string path, string? token = null) =>
        h.Api.DownloadAsync($"{Base}/{id}/{path}", token ?? h.AdminToken);
}
