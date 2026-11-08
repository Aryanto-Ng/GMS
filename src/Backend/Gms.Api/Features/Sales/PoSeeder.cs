using Gms.Api.Common;
using Gms.Api.Hosting;
using Gms.Api.Security;
using Gms.Domain.Inventory;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Features.Sales;

/// <summary>
/// Data demo Customer PO (hanya bila <c>Seed:DemoData</c>): satu PO Confirmed tanpa nomor PO customer, satu PO Confirmed lengkap
/// dengan baris terlambat, satu Draft, serta stok FG awal agar alokasi dapat dicoba. Idempoten.
/// Stok awal ditulis sebagai OpeningBalance di ledger + saldo (modul Stok FG kelak membaca tabel yang sama).
/// </summary>
public sealed class PoSeeder(
    GmsDbContext db, NumberSequences sequences, TimeProvider time, AppTimeZone zone, IOptions<SeedOptions> options) : IDataSeeder
{
    public int Order => 30;

    public async Task SeedAsync(CancellationToken ct)
    {
        if (!options.Value.DemoData || await db.PurchaseOrders.AnyAsync(ct))
        {
            return;
        }

        var customers = await db.Customers.Where(c => c.Name == "PT Maju Jaya" || c.Name == "PT Karya Mandiri").ToDictionaryAsync(c => c.Name, ct);
        var kmj = await db.Fgs.SingleOrDefaultAsync(f => f.Code == "KMJ-001", ct);
        var cln = await db.Fgs.SingleOrDefaultAsync(f => f.Code == "CLN-001", ct);
        if (kmj is null || cln is null || customers.Count < 2)
        {
            return; // data master demo belum ada
        }

        var colors = await db.Colors.ToDictionaryAsync(c => c.Code, ct);
        var sizes = await db.Sizes.ToDictionaryAsync(s => s.Code, ct);
        var today = zone.Today(time);
        var now = time.GetUtcNow();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1) Confirmed, nomor PO customer belum ada, komitmen sudah ada.
        db.PurchaseOrders.Add(NewPo(await sequences.NextMonthlyCodeAsync("PO", ct), customers["PT Maju Jaya"].Id, null, today.AddDays(-3), PoStatus.Confirmed, now,
            Line(kmj, colors["NAVY"], sizes["M"], today.AddDays(14), 200, now), Line(kmj, colors["NAVY"], sizes["L"], today.AddDays(14), 150, now),
            Line(kmj, colors["ORANGE"], sizes["M"], today.AddDays(21), 80, now)));

        // 2) Confirmed lengkap, satu baris sudah lewat tanggal kirim (terlambat).
        db.PurchaseOrders.Add(NewPo(await sequences.NextMonthlyCodeAsync("PO", ct), customers["PT Karya Mandiri"].Id, "PO/KM/2026/118", today.AddDays(-20), PoStatus.Confirmed, now,
            Line(cln, colors["BLACK"], sizes["L"], today.AddDays(-2), 120, now), Line(cln, colors["BLACK"], sizes["XL"], today.AddDays(10), 100, now)));

        // 3) Draft.
        db.PurchaseOrders.Add(NewPo(await sequences.NextMonthlyCodeAsync("PO", ct), customers["PT Maju Jaya"].Id, null, today, PoStatus.Draft, now,
            Line(kmj, colors["GREY"], sizes["S"], today.AddDays(30), 60, now)));

        // Stok awal contoh.
        foreach (var (fg, color, size, qty) in new[]
                 {
                     (kmj, colors["NAVY"], sizes["M"], 120), (kmj, colors["NAVY"], sizes["L"], 60), (cln, colors["BLACK"], sizes["L"], 40),
                 })
        {
            db.StockBalances.Add(new StockBalance { FgId = fg.Id, ColorId = color.Id, SizeId = size.Id, OnHand = qty, Allocated = 0 });
            db.StockLedger.Add(new StockLedgerEntry
            {
                FgId = fg.Id, ColorId = color.Id, SizeId = size.Id, Kind = StockLedgerKind.OpeningBalance, Qty = qty,
                SourceType = "OpeningBalance", EntryDate = today, Reason = "Saldo awal (data demo)", CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static PurchaseOrder NewPo(string code, Guid customerId, string? customerPoNumber, DateOnly poDate, PoStatus status, DateTimeOffset now, params PoLine[] lines)
    {
        var po = new PurchaseOrder
        {
            Code = code, CustomerId = customerId, CustomerPoNumber = customerPoNumber, PoDate = poDate, Status = status, CreatedAt = now,
            ConfirmedAt = status == PoStatus.Draft ? null : now,
        };
        foreach (var line in lines)
        {
            line.PoId = po.Id;
            po.Lines.Add(line);
        }

        return po;
    }

    private static PoLine Line(Domain.Master.Fg fg, Domain.Master.Color color, Domain.Master.Size size, DateOnly delivery, int qty, DateTimeOffset now) =>
        new() { FgId = fg.Id, ColorId = color.Id, SizeId = size.Id, DeliveryDate = delivery, Qty = qty, CreatedAt = now };
}
