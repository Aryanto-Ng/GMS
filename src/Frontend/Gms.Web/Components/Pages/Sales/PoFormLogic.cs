using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Web.Components.Shared;

namespace Gms.Web.Components.Pages.Sales;

/// <summary>Satu blok kebutuhan = FG + Color + Tanggal Kirim, dengan satu baris Qty per Size Berlaku FG.</summary>
public sealed class PoBlock
{
    public Guid Key { get; } = Guid.NewGuid();
    public Guid FgId { get; init; }
    public string FgLabel { get; init; } = string.Empty;
    public Guid ColorId { get; init; }
    public string ColorLabel { get; init; } = string.Empty;
    public string DeliveryText { get; set; } = string.Empty;
    public IReadOnlyList<SizeRef> Sizes { get; init; } = [];
    public MatrixValues Values { get; } = new();

    /// <summary>Baris yang sudah ada di server (mode ubah): id, batas minimum, dan terkunci, per Size.</summary>
    public Dictionary<Guid, (Guid LineId, int Min, bool Locked)> Existing { get; } = [];

    public DateOnly? Delivery => DateOnly.TryParseExact(DeliveryText, "yyyy-MM-dd", out var d) ? d : null;

    public int Total => Sizes.Sum(s => Values.Get("qty", s.Id.ToString()) ?? 0);
}

/// <summary>Logika murni form PO: konversi blok ↔ baris, deteksi duplikat, dan total. Aturan stok/komitmen tetap hasil server.</summary>
public static class PoFormLogic
{
    public const int WarnQty = 100_000;
    public const int MaxQty = 1_000_000;

    public static string CellKey(Guid sizeId) => sizeId.ToString();

    public static int? QtyOf(PoBlock block, Guid sizeId) => block.Values.Get("qty", CellKey(sizeId));

    public static void SetQty(PoBlock block, Guid sizeId, int? qty) => block.Values.Set("qty", CellKey(sizeId), qty);

    /// <summary>Baris yang dikirim: sel terisi (&gt; 0). Baris lama yang dikosongkan tidak ikut dikirim (server menghapusnya bila diizinkan).</summary>
    public static IReadOnlyList<SavePoLine> ToLines(IEnumerable<PoBlock> blocks)
    {
        var lines = new List<SavePoLine>();
        foreach (var block in blocks)
        {
            foreach (var size in block.Sizes)
            {
                if (QtyOf(block, size.Id) is not { } qty || qty <= 0 || block.Delivery is not { } date)
                {
                    continue;
                }

                Guid? lineId = block.Existing.TryGetValue(size.Id, out var existing) ? existing.LineId : null;
                lines.Add(new SavePoLine(lineId, block.FgId, block.ColorId, size.Id, date, qty, null));
            }
        }

        return lines;
    }

    /// <summary>Mengelompokkan baris PO menjadi blok (FG + Color + Tanggal Kirim) untuk mode ubah.</summary>
    public static List<PoBlock> FromDto(PoDto po, Func<Guid, IReadOnlyList<SizeRef>> sizesOfFg)
    {
        var blocks = new List<PoBlock>();
        foreach (var group in po.Lines.GroupBy(l => (l.FgId, l.ColorSize.ColorId, l.DeliveryDate)))
        {
            var first = group.First();
            var block = new PoBlock
            {
                FgId = first.FgId, FgLabel = $"{first.FgCode} {first.FgName}", ColorId = first.ColorSize.ColorId, ColorLabel = first.ColorSize.ColorName,
                DeliveryText = first.DeliveryDate.ToString("yyyy-MM-dd"), Sizes = Merge(sizesOfFg(first.FgId), group.Select(l => l.ColorSize)),
            };
            foreach (var line in group)
            {
                SetQty(block, line.ColorSize.SizeId, line.Qty);
                block.Existing[line.ColorSize.SizeId] = (line.Id, line.MinAllowedQty, line.IsLocked);
            }

            blocks.Add(block);
        }

        return blocks;
    }

    private static IReadOnlyList<SizeRef> Merge(IReadOnlyList<SizeRef> fgSizes, IEnumerable<ColorSizeRef> used)
    {
        var list = fgSizes.ToList();
        foreach (var u in used.Where(u => list.All(s => s.Id != u.SizeId)))
        {
            list.Add(new SizeRef(u.SizeId, u.SizeCode, u.SizeName, u.SizeSortOrder)); // Size lama yang tidak lagi berlaku tetap tampil
        }

        return list.OrderBy(s => s.SortOrder).ToList();
    }

    /// <summary>Blok yang membentuk kombinasi FG + Color + Size + Tanggal Kirim ganda dengan blok lain.</summary>
    public static IReadOnlySet<Guid> DuplicateBlocks(IReadOnlyList<PoBlock> blocks)
    {
        var seen = new Dictionary<(Guid, Guid, Guid, DateOnly), Guid>();
        var dup = new HashSet<Guid>();
        foreach (var block in blocks.Where(b => b.Delivery is not null))
        {
            foreach (var size in block.Sizes.Where(s => (QtyOf(block, s.Id) ?? 0) > 0))
            {
                var key = (block.FgId, block.ColorId, size.Id, block.Delivery!.Value);
                if (seen.TryGetValue(key, out var other) && other != block.Key)
                {
                    dup.Add(block.Key);
                    dup.Add(other);
                }
                else
                {
                    seen[key] = block.Key;
                }
            }
        }

        return dup;
    }

    public static int GrandTotal(IEnumerable<PoBlock> blocks) => blocks.Sum(b => b.Total);

    public static bool HasQtyAbove(IEnumerable<PoBlock> blocks, int limit) => blocks.Any(b => b.Sizes.Any(s => (QtyOf(b, s.Id) ?? 0) > limit));

    /// <summary>Sel yang melebihi batas keras (1.000.000) dianggap galat pada grid.</summary>
    public static Func<string, string, MatrixCellInfo> CellInfo => (_, _) => new MatrixCellInfo(Max: MaxQty);
}
