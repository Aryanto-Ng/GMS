using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;
using static Gms.Api.Tests.Infrastructure.ProdHelpers;

namespace Gms.Api.Tests.Infrastructure;

public static class InvHelpers
{
    private const string Base = "/api/v1/handovers/fg";

    /// <summary>Memproduksi qty penuh melalui rantai CUT → EMB (kirim+terima) → SEW agar output langkah terakhir tersedia untuk serah terima.</summary>
    public static async Task ProduceAsync(this TestHost h, ProdSetup s, int qtySmall, int qtyMedium = 0)
    {
        var worker = await h.NewWorkerAsync();
        var vendor = await h.NewVendorAsync();
        var cells = new List<(Guid Line, int Qty)>();
        if (qtySmall > 0) { cells.Add((s.LineS, qtySmall)); }
        if (qtyMedium > 0) { cells.Add((s.LineM, qtyMedium)); }

        await h.OutAsync(s, s.Cut, worker.Id, cells.ToArray());
        await h.SendAsync(s, vendor.Id, cells.ToArray());
        await h.ReceiveAsync(s, vendor.Id, cells.ToArray());
        await h.OutAsync(s, s.Sew, worker.Id, cells.ToArray());
    }

    public static Task<ApiResponse> PostHandoverAsync(this TestHost h, ProdSetup s, IEnumerable<(Guid Line, int Qty)> cells,
        DateOnly? date = null, string? docNo = null, string? token = null) =>
        h.Api.PostAsync(Base, new SaveHandoverFgRequest(s.Wo.Id, date ?? Today(), docNo, "Gudang Comfi", null, Cells(cells.ToArray()).ToList()), token ?? h.AdminToken);

    public static async Task<HandoverFgResultDto> HandoverAsync(this TestHost h, ProdSetup s, params (Guid Line, int Qty)[] cells) =>
        (await h.PostHandoverAsync(s, cells)).Ok("serah terima FG").As<HandoverFgResultDto>();

    public static async Task<HandoverFgDto> GetHandoverAsync(this TestHost h, Guid id) =>
        (await h.Api.GetAsync($"{Base}/{id}", h.AdminToken)).Ok().As<HandoverFgDto>();

    public static Task<ApiResponse> VoidHandoverAsync(this TestHost h, Guid id, string? reason = "salah input", string? token = null) =>
        h.Api.PostAsync($"{Base}/{id}/void", new ReasonRequest(reason ?? "", 0), token ?? h.AdminToken);

    public static async Task<StockRow?> StockRowAsync(this TestHost h, ProdSetup s, Guid sizeId)
    {
        var result = (await h.Api.GetAsync($"/api/v1/stock?fgId={s.World.Fg.Fg.Id}&colorId={s.World.Color.Id}&sizeId={sizeId}&hanyaAdaStok=false", h.AdminToken)).Ok().As<StockListResult>();
        return result.Rows.Items.SingleOrDefault();
    }

    public static async Task<(int Allocated, int Delivered, int InProduction)> PoLineAsync(this TestHost h, ProdSetup s, Guid poLineId)
    {
        await using var db = h.CreateDbContext();
        var line = await db.PoLines.AsNoTracking().SingleAsync(l => l.Id == poLineId);
        return (line.AllocatedQty, line.DeliveredQty, line.InProductionQty);
    }

    /// <summary>Invarian stok (ADR-004): Σ ledger stok = Fisik; Teralokasi = Σ alokasi baris PO = Σ ledger alokasi; 0 ≤ Teralokasi ≤ Fisik.</summary>
    public static async Task AssertStockConsistentAsync(this TestHost h, Guid fgId, Guid colorId, Guid sizeId)
    {
        await using var db = h.CreateDbContext();
        var stock = await db.StockBalances.AsNoTracking().SingleAsync(s => s.FgId == fgId && s.ColorId == colorId && s.SizeId == sizeId);
        var ledger = await db.StockLedger.AsNoTracking().Where(e => e.FgId == fgId && e.ColorId == colorId && e.SizeId == sizeId).SumAsync(e => (int?)e.Qty) ?? 0;
        Assert.Equal(stock.OnHand, ledger);
        await h.AssertStockInvariantAsync(fgId, colorId, sizeId);
    }

    public static Task<ApiResponse> UploadOpeningAsync(this TestHost h, string csv, bool dryRun, string? token = null, string header = "fg_code,color_code,size_code,qty,date") =>
        h.Api.PostFileAsync($"/api/v1/stock/opening-balance/import?dryRun={dryRun.ToString().ToLowerInvariant()}", "saldo.csv",
            System.Text.Encoding.UTF8.GetBytes(header + "\n" + csv), token ?? h.AdminToken);
}
