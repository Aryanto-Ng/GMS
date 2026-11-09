using Gms.Domain.Inventory;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Tests.Infrastructure;

/// <summary>Helper tes Customer PO: dunia uji (customer + FG siap + color), pembuat PO, dan pengatur stok langsung di database.</summary>
public static class PoHelpers
{
    private const string Base = "/api/v1/purchase-orders";

    /// <summary>"Hari ini" menurut zona aplikasi (WIB, UTC+7) ditambah selisih hari.</summary>
    public static DateOnly Today(int offsetDays = 0) => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(offsetDays);

    public sealed record PoWorld(CustomerDto Customer, MasterHelpers.ReadyFg Fg, ColorDto Color)
    {
        public SizeDto Small => Fg.Small;
        public SizeDto Medium => Fg.Medium;

        public SavePoLine Line(SizeDto size, int qty, int deliveryOffsetDays = 30, Guid? id = null) =>
            new(id, Fg.Fg.Id, Color.Id, size.Id, Today(deliveryOffsetDays), qty, null);
    }

    public static async Task<PoWorld> NewPoWorldAsync(this TestHost h)
    {
        var customer = await h.NewCustomerAsync();
        var fg = await h.NewReadyFgAsync(customer.Id);
        var color = await h.NewColorAsync();
        return new PoWorld(customer, fg, color);
    }

    public static Task<ApiResponse> PostPoAsync(
        this TestHost h, PoWorld w, IEnumerable<SavePoLine> lines, bool confirm = false, string? customerPoNumber = null, string? token = null) =>
        h.Api.PostAsync(Base,
            new SavePoRequest(w.Customer.Id, Today(), customerPoNumber, "uji", lines.ToList(), confirm), token ?? h.AdminToken);

    public static async Task<PoDto> NewPoAsync(
        this TestHost h, PoWorld w, IEnumerable<SavePoLine> lines, bool confirm = true, string? customerPoNumber = null) =>
        (await h.PostPoAsync(w, lines, confirm, customerPoNumber)).Ok("buat PO").As<PoDto>();

    public static async Task<PoDto> GetPoAsync(this TestHost h, Guid id) =>
        (await h.Api.GetAsync($"{Base}/{id}", h.AdminToken)).Ok().As<PoDto>();

    public static SavePoRequest ToRequest(this PoDto po, IEnumerable<SavePoLine>? lines = null, string? customerPoNumber = "keep", string? notes = "keep") =>
        new(po.CustomerId, po.PoDate, customerPoNumber == "keep" ? po.CustomerPoNumber : customerPoNumber,
            notes == "keep" ? po.Notes : notes,
            (lines ?? po.Lines.Select(l => new SavePoLine(l.Id, l.FgId, l.ColorSize.ColorId, l.ColorSize.SizeId, l.DeliveryDate, l.Qty, l.Notes))).ToList(),
            false, po.RowVersion);

    public static Task<ApiResponse> PutPoAsync(this TestHost h, Guid id, SavePoRequest request, string? token = null) =>
        h.Api.PutAsync($"{Base}/{id}", request, token ?? h.AdminToken);

    public static SavePoLine AsLine(this PoLineDto l, int? qty = null, DateOnly? delivery = null) =>
        new(l.Id, l.FgId, l.ColorSize.ColorId, l.ColorSize.SizeId, delivery ?? l.DeliveryDate, qty ?? l.Qty, l.Notes);

    // ===== Stok (modul Stok FG belum ada; baris stok ditulis langsung seperti yang kelak dilakukan modul itu) =====

    public static async Task SetStockAsync(this TestHost h, Guid fgId, Guid colorId, Guid sizeId, int onHand)
    {
        await using var db = h.CreateDbContext();
        var stock = await db.StockBalances.SingleOrDefaultAsync(s => s.FgId == fgId && s.ColorId == colorId && s.SizeId == sizeId);
        if (stock is null)
        {
            db.StockBalances.Add(new StockBalance { FgId = fgId, ColorId = colorId, SizeId = sizeId, OnHand = onHand });
        }
        else
        {
            stock.OnHand = onHand;
        }

        db.StockLedger.Add(new StockLedgerEntry
        {
            FgId = fgId, ColorId = colorId, SizeId = sizeId, Kind = StockLedgerKind.OpeningBalance, Qty = Math.Max(onHand, 1),
            SourceType = "Test", EntryDate = Today(), CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public static async Task<(int OnHand, int Allocated)> StockAsync(this TestHost h, Guid fgId, Guid colorId, Guid sizeId)
    {
        await using var db = h.CreateDbContext();
        var stock = await db.StockBalances.AsNoTracking().SingleOrDefaultAsync(s => s.FgId == fgId && s.ColorId == colorId && s.SizeId == sizeId);
        return (stock?.OnHand ?? 0, stock?.Allocated ?? 0);
    }

    /// <summary>
    /// Invarian inti (ADR-004): untuk satu kombinasi stok, Teralokasi di saldo = jumlah alokasi aktif seluruh baris PO,
    /// dan 0 ≤ Teralokasi ≤ Fisik. Dipanggil setelah tes konkurensi.
    /// </summary>
    public static async Task AssertStockInvariantAsync(this TestHost h, Guid fgId, Guid colorId, Guid sizeId)
    {
        await using var db = h.CreateDbContext();
        var stock = await db.StockBalances.AsNoTracking().SingleAsync(s => s.FgId == fgId && s.ColorId == colorId && s.SizeId == sizeId);
        var sumLines = await db.PoLines.AsNoTracking()
            .Where(l => l.FgId == fgId && l.ColorId == colorId && l.SizeId == sizeId)
            .SumAsync(l => (int?)l.AllocatedQty) ?? 0;
        var sumLedger = await db.PoAllocations.AsNoTracking()
            .Join(db.PoLines.Where(l => l.FgId == fgId && l.ColorId == colorId && l.SizeId == sizeId), a => a.PoLineId, l => l.Id, (a, l) => a)
            .SumAsync(a => (int?)(a.Kind == AllocationKind.Allocate || a.Kind == AllocationKind.ReverseConsume ? a.Qty : -a.Qty)) ?? 0;

        Assert.Equal(sumLines, stock.Allocated);
        Assert.Equal(sumLines, sumLedger);
        Assert.InRange(stock.Allocated, 0, stock.OnHand);
    }

    public static async Task UpdateLineAsync(this TestHost h, Guid lineId, Action<Gms.Domain.Sales.PoLine> change)
    {
        await using var db = h.CreateDbContext();
        var line = await db.PoLines.SingleAsync(l => l.Id == lineId);
        change(line);
        await db.SaveChangesAsync();
    }
}
