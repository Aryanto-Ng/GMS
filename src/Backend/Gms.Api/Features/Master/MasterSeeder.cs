using Gms.Api.Common;
using Gms.Api.Hosting;
using Gms.Api.Security;
using Gms.Domain.Common;
using Gms.Domain.Master;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gms.Api.Features.Master;

/// <summary>
/// Seeder Master Data (idempoten). Selalu: Customer "STOK INTERNAL" (untuk PO produksi stok, FSD Customer PO E2).
/// Bila <c>Seed:DemoData</c> = true (Development/UAT): data contoh lengkap supaya Frontend/QA dapat menguji tanpa input manual.
/// </summary>
public sealed class MasterSeeder(
    GmsDbContext db,
    NumberSequences sequences,
    TimeProvider time,
    IOptions<SeedOptions> options) : IDataSeeder
{
    public const string InternalStockCustomerName = "STOK INTERNAL";

    public int Order => 20;

    public async Task SeedAsync(CancellationToken ct)
    {
        await EnsureCustomerAsync(InternalStockCustomerName, ct);
        if (options.Value.DemoData)
        {
            await SeedDemoAsync(ct);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string name, CancellationToken ct)
    {
        var existing = await db.Customers.SingleOrDefaultAsync(c => c.Name == name, ct);
        if (existing is not null)
        {
            return existing;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var customer = new Customer
        {
            Code = await sequences.NextCodeAsync("CUS", ct),
            Name = name,
            CreatedAt = time.GetUtcNow(),
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return customer;
    }

    private async Task SeedDemoAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();

        // Size, Color, Satuan, Kategori
        var sizes = await EnsureAsync(db.Sizes, ct,
            ("S", new Size { Code = "S", Name = "Small", SortOrder = 10 }),
            ("M", new Size { Code = "M", Name = "Medium", SortOrder = 20 }),
            ("L", new Size { Code = "L", Name = "Large", SortOrder = 30 }),
            ("XL", new Size { Code = "XL", Name = "Extra Large", SortOrder = 40 }),
            ("2XL", new Size { Code = "2XL", Name = "2X Large", SortOrder = 50 }),
            ("3XL", new Size { Code = "3XL", Name = "3X Large", SortOrder = 60 }));
        await EnsureAsync(db.Colors, ct,
            ("NAVY", new Color { Code = "NAVY", Name = "Navy", Hex = "#1F3A5F" }),
            ("ORANGE", new Color { Code = "ORANGE", Name = "Orange", Hex = "#F28C28" }),
            ("GREY", new Color { Code = "GREY", Name = "Abu-abu", Hex = "#808080" }),
            ("BLACK", new Color { Code = "BLACK", Name = "Hitam", Hex = "#000000" }));
        var uoms = await EnsureAsync(db.Uoms, ct,
            ("MTR", new Uom { Code = "MTR", Name = "Meter" }),
            ("PCS", new Uom { Code = "PCS", Name = "Pieces" }),
            ("KG", new Uom { Code = "KG", Name = "Kilogram" }),
            ("CONE", new Uom { Code = "CONE", Name = "Cone" }));
        var categories = await EnsureAsync(db.MaterialCategories, ct,
            ("KAIN", new MaterialCategory { Code = "KAIN", Name = "Kain" }),
            ("BENANG", new MaterialCategory { Code = "BENANG", Name = "Benang" }),
            ("AKS", new MaterialCategory { Code = "AKS", Name = "Aksesoris" }));
        await EnsureAsync(db.Materials, ct,
            ("KN-NAVY", new Material { Code = "KN-NAVY", Name = "Kain Drill Navy", CategoryId = categories["KAIN"].Id, UomId = uoms["MTR"].Id }),
            ("KN-ORANGE", new Material { Code = "KN-ORANGE", Name = "Kain Drill Orange", CategoryId = categories["KAIN"].Id, UomId = uoms["MTR"].Id }),
            ("BN-HITAM", new Material { Code = "BN-HITAM", Name = "Benang Hitam", CategoryId = categories["BENANG"].Id, UomId = uoms["CONE"].Id }),
            ("KCG-01", new Material { Code = "KCG-01", Name = "Kancing Baju 18L", CategoryId = categories["AKS"].Id, UomId = uoms["PCS"].Id }),
            ("RSL-01", new Material { Code = "RSL-01", Name = "Resleting Besi", CategoryId = categories["AKS"].Id, UomId = uoms["PCS"].Id }));

        var processes = await EnsureAsync(db.Processes, ct,
            ("CUT", new Process { Code = "CUT", Name = "Cutting", Type = ProcessType.Internal }),
            ("EMB", new Process { Code = "EMB", Name = "Bordir", Type = ProcessType.Vendor }),
            ("SEW", new Process { Code = "SEW", Name = "Sewing", Type = ProcessType.Internal }),
            ("QC", new Process { Code = "QC", Name = "Quality Control", Type = ProcessType.Internal }),
            ("FIN", new Process { Code = "FIN", Name = "Finishing", Type = ProcessType.Internal }),
            ("PCK", new Process { Code = "PCK", Name = "Packing", Type = ProcessType.Internal }));

        var maju = await EnsureCustomerAsync("PT Maju Jaya", ct);
        await EnsureCustomerAsync("PT Karya Mandiri", ct);

        if (!await db.Vendors.AnyAsync(v => v.Name == "CV Bordir Indah", ct))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Vendors.Add(new Vendor
            {
                Code = await sequences.NextCodeAsync("VND", ct), Name = "CV Bordir Indah", ServiceType = "Bordir",
                ContactPerson = "Bu Sari", Phone = "081200000000", CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        if (!await db.Workers.AnyAsync(ct))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var names = new[]
            {
                ("Budi Santoso", "SEW"), ("Siti Rahayu", "SEW"), ("Andi Pratama", "CUT"),
                ("Dewi Lestari", "QC"), ("Joko Widodo", "PCK"), ("Rina Marlina", "SEW"),
            };
            foreach (var (name, process) in names)
            {
                db.Workers.Add(new Worker
                {
                    Code = await sequences.NextCodeAsync("PKJ", ct), Name = name, MainProcessId = processes[process].Id,
                    JoinDate = new DateOnly(2025, 1, 6), CreatedAt = now,
                });
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // FG contoh: dua FG lengkap (Siap Produksi) dan satu FG belum lengkap (untuk menguji status kesiapan).
        await EnsureFgAsync("KMJ-001", "Kemeja Wearpak Lengan Panjang", "Kemeja", maju.Id,
            ["S", "M", "L", "XL", "2XL"], ["CUT", "EMB", "SEW", "QC", "PCK"],
            new Dictionary<string, decimal> { ["CUT"] = 350m, ["SEW"] = 2500m, ["QC"] = 400m, ["PCK"] = 300m }, sizes, processes, ct);
        await EnsureFgAsync("CLN-001", "Celana Wearpak", "Celana", maju.Id,
            ["M", "L", "XL", "2XL", "3XL"], ["CUT", "SEW", "QC", "FIN", "PCK"],
            new Dictionary<string, decimal> { ["CUT"] = 300m, ["SEW"] = 2200.5m, ["QC"] = 350m, ["FIN"] = 450m, ["PCK"] = 300m }, sizes, processes, ct);
        await EnsureFgAsync("COV-001", "Coverall (belum lengkap)", "Coverall", null,
            ["M", "L", "XL"], ["CUT", "SEW"], new Dictionary<string, decimal> { ["CUT"] = 500m }, sizes, processes, ct);
    }

    /// <summary>Menambahkan entitas yang belum ada (berdasarkan kode) dan mengembalikan seluruhnya per kode.</summary>
    private async Task<Dictionary<string, T>> EnsureAsync<T>(DbSet<T> set, CancellationToken ct, params (string Code, T Entity)[] items)
        where T : MasterEntity, ICodedEntity
    {
        var codes = items.Select(i => i.Code).ToList();
        var existing = await set.Where(e => codes.Contains(e.Code)).ToDictionaryAsync(e => e.Code, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var (code, entity) in items)
        {
            if (!existing.ContainsKey(code))
            {
                entity.CreatedAt = time.GetUtcNow();
                set.Add(entity);
                existing[code] = entity;
            }
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }

    private async Task EnsureFgAsync(
        string code, string name, string kind, Guid? customerId, string[] sizeCodes, string[] processCodes,
        Dictionary<string, decimal> baseRates, Dictionary<string, Size> sizes, Dictionary<string, Process> processes, CancellationToken ct)
    {
        if (await db.Fgs.AnyAsync(f => f.Code == code, ct))
        {
            return;
        }

        var fg = new Fg { Code = code, Name = name, Kind = kind, CustomerId = customerId, CreatedAt = time.GetUtcNow() };
        foreach (var sizeCode in sizeCodes)
        {
            fg.Sizes.Add(new FgSize { FgId = fg.Id, SizeId = sizes[sizeCode].Id });
        }

        for (var i = 0; i < processCodes.Length; i++)
        {
            fg.RoutingSteps.Add(new FgRoutingStep { FgId = fg.Id, ProcessId = processes[processCodes[i]].Id, Seq = i + 1 });
        }

        // Tarif naik 10% per tingkat size (agar terlihat tarif berbeda per size).
        foreach (var (processCode, rate) in baseRates)
        {
            for (var s = 0; s < sizeCodes.Length; s++)
            {
                var value = decimal.Round(rate * (1m + 0.10m * s), 5);
                fg.Tariffs.Add(new FgTariff { FgId = fg.Id, ProcessId = processes[processCode].Id, SizeId = sizes[sizeCodes[s]].Id, Rate = value });
            }
        }

        db.Fgs.Add(fg);
        await db.SaveChangesAsync(ct);
    }
}
