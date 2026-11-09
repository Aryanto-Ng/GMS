using Gms.Domain.Production;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.WorkOrders;
using Microsoft.EntityFrameworkCore;
using static Gms.Api.Tests.Infrastructure.PoHelpers;

namespace Gms.Api.Tests.Infrastructure;

/// <summary>Helper tes Work Order: pembuat WO, aksi status, dan penyuntik keadaan yang kelak dihasilkan modul Output/Serah Terima.</summary>
public static class WoHelpers
{
    private const string Base = "/api/v1/work-orders";

    public static Guid PoLineId(this PoDto po, SizeDto size) => po.Lines.Single(l => l.ColorSize.SizeId == size.Id).Id;

    public static SaveWoRequest WoRequest(this PoWorld w, PoDto po, IEnumerable<(Guid PoLineId, int Qty)> cells,
        DateOnly? start = null, DateOnly? target = null, string? notes = null, uint rowVersion = 0) =>
        new(po.Id, w.Fg.Fg.Id, start, target, notes, cells.Select(c => new SaveWoLine(c.PoLineId, c.Qty)).ToList(), rowVersion);

    public static Task<ApiResponse> PostWoAsync(this TestHost h, SaveWoRequest request, string? token = null) =>
        h.Api.PostAsync(Base, request, token ?? h.AdminToken);

    public static async Task<WoDto> NewWoAsync(this TestHost h, PoWorld w, PoDto po, params (Guid PoLineId, int Qty)[] cells) =>
        (await h.PostWoAsync(w.WoRequest(po, cells))).Ok("buat WO").As<WoDto>();

    /// <summary>PO terkonfirmasi dengan satu baris S dan satu baris M, lalu WO Draft yang mengambil sebagian.</summary>
    public static async Task<(PoWorld World, PoDto Po, WoDto Wo)> NewWoSetupAsync(
        this TestHost h, int poQty = 100, int woQtySmall = 60, int woQtyMedium = 40)
    {
        var w = await h.NewPoWorldAsync();
        var po = await h.NewPoAsync(w, [w.Line(w.Small, poQty), w.Line(w.Medium, poQty)]);
        var wo = await h.NewWoAsync(w, po, (po.PoLineId(w.Small), woQtySmall), (po.PoLineId(w.Medium), woQtyMedium));
        return (w, po, wo);
    }

    public static async Task<WoDto> GetWoAsync(this TestHost h, Guid id) =>
        (await h.Api.GetAsync($"{Base}/{id}", h.AdminToken)).Ok().As<WoDto>();

    public static Task<ApiResponse> PutWoAsync(this TestHost h, Guid id, SaveWoRequest request, string? token = null) =>
        h.Api.PutAsync($"{Base}/{id}", request, token ?? h.AdminToken);

    public static Task<ApiResponse> WoActionAsync(this TestHost h, Guid id, string action, WoDto current, string? reason = null, string? token = null) =>
        h.Api.PostAsync($"{Base}/{id}/{action}", new WoActionRequest(reason, current.RowVersion), token ?? h.AdminToken);

    /// <summary>Menjalankan aksi memakai versi terbaru dan memastikan sukses.</summary>
    public static async Task<WoDto> WoDoAsync(this TestHost h, Guid id, string action, string? reason = null) =>
        (await h.WoActionAsync(id, action, await h.GetWoAsync(id), reason)).Ok(action).As<WoDto>();

    public static Task<ApiResponse> PutQuantitiesAsync(this TestHost h, WoDto wo, IEnumerable<(Guid PoLineId, int Qty)> cells) =>
        h.Api.PutAsync($"{Base}/{wo.Id}/quantities",
            new UpdateWoQuantitiesRequest(cells.Select(c => new SaveWoLine(c.PoLineId, c.Qty)).ToList(), wo.RowVersion), h.AdminToken);

    /// <summary>Membuat WO Released siap pakai (semua sel dari setup).</summary>
    public static async Task<(PoWorld World, PoDto Po, WoDto Wo)> NewReleasedWoAsync(
        this TestHost h, int poQty = 100, int woQtySmall = 60, int woQtyMedium = 40)
    {
        var (w, po, wo) = await h.NewWoSetupAsync(poQty, woQtySmall, woQtyMedium);
        return (w, po, await h.WoDoAsync(wo.Id, "release"));
    }

    // ===== Penyuntik keadaan (modul Output/Serah Terima belum ada) =====

    public static async Task SetWoStatusAsync(this TestHost h, Guid woId, WoStatus status)
    {
        await using var db = h.CreateDbContext();
        var wo = await db.WorkOrders.SingleAsync(x => x.Id == woId);
        wo.Status = status;
        wo.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    public static async Task SetStepBalanceAsync(this TestHost h, Guid woLineId, int seq, int qty, int sent = 0)
    {
        await using var db = h.CreateDbContext();
        var balance = await db.WoStepBalances.SingleOrDefaultAsync(b => b.WoLineId == woLineId && b.Seq == seq);
        if (balance is null)
        {
            db.WoStepBalances.Add(new WoStepBalance { WoLineId = woLineId, Seq = seq, Qty = qty, SentQty = sent });
        }
        else
        {
            balance.Qty = qty;
            balance.SentQty = sent;
        }

        await db.SaveChangesAsync();
    }

    public static async Task SetHandedOverAsync(this TestHost h, Guid woLineId, int qty)
    {
        await using var db = h.CreateDbContext();
        var line = await db.WoLines.SingleAsync(l => l.Id == woLineId);
        line.HandedOverQty = qty;
        await db.SaveChangesAsync();
    }

    /// <summary>Invarian ADR-004: Dalam Produksi baris PO = Σ max(qty WO − diserahterimakan, 0) untuk WO aktif; Sisa Rencana ≥ 0.</summary>
    public static async Task AssertInProductionInvariantAsync(this TestHost h, Guid poId)
    {
        await using var db = h.CreateDbContext();
        var lines = await db.PoLines.AsNoTracking().Where(l => l.PoId == poId).ToListAsync();
        var active = new[] { WoStatus.Draft, WoStatus.Released, WoStatus.InProgress, WoStatus.Completed };
        foreach (var line in lines)
        {
            var expected = await db.WoLines.Where(l => l.PoLineId == line.Id && active.Contains(db.WorkOrders.First(w => w.Id == l.WoId).Status))
                .SumAsync(l => (int?)(l.Qty - l.HandedOverQty > 0 ? l.Qty - l.HandedOverQty : 0)) ?? 0;
            Assert.Equal(expected, line.InProductionQty);
            Assert.True(line.RemainingPlanQty >= 0);
        }
    }
}
