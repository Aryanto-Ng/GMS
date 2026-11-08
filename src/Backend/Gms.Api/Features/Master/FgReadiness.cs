using System.Linq.Expressions;
using Gms.Domain.Master;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Master;

/// <summary>
/// Status kesiapan FG "Siap Produksi" (FSD Master Data 4.11): FG aktif, minimal 1 Size Berlaku, routing berisi minimal
/// 1 langkah dengan semua proses aktif, dan setiap langkah Internal memiliki tarif (&gt; 0) untuk SETIAP Size Berlaku.
/// Karena FK komposit menjamin tarif hanya mungkin untuk langkah+size yang sah, kelengkapan cukup diperiksa dengan
/// <c>jumlah tarif = langkah Internal × size</c>.
/// </summary>
public static class FgReadiness
{
    /// <summary>Predikat set-based (dapat dipakai di WHERE untuk daftar dan filter).</summary>
    public static Expression<Func<Fg, bool>> IsReady(GmsDbContext db) =>
        f => f.IsActive
             && db.FgSizes.Count(s => s.FgId == f.Id) > 0
             && db.FgRoutingSteps.Count(s => s.FgId == f.Id) > 0
             && !db.FgRoutingSteps.Any(s => s.FgId == f.Id && !db.Processes.Any(p => p.Id == s.ProcessId && p.IsActive))
             && db.FgTariffs.Count(t => t.FgId == f.Id)
             == db.FgRoutingSteps.Count(s => s.FgId == f.Id && db.Processes.Any(p => p.Id == s.ProcessId && p.Type == ProcessType.Internal))
             * db.FgSizes.Count(s => s.FgId == f.Id);

    /// <summary>Himpunan id FG (dari <paramref name="ids"/>) yang Siap Produksi.</summary>
    public static async Task<HashSet<Guid>> ReadyIdsAsync(GmsDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var ready = await db.Fgs.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Where(IsReady(db))
            .Select(f => f.Id)
            .ToListAsync(ct);
        return ready.ToHashSet();
    }

    /// <summary>Daftar periksa rinci dengan alasan spesifik per butir (AC-MD-17).</summary>
    public static async Task<(bool IsReady, List<ReadinessItem> Items)> EvaluateAsync(GmsDbContext db, Guid fgId, CancellationToken ct)
    {
        var fg = await db.Fgs.AsNoTracking().Where(f => f.Id == fgId).Select(f => new { f.IsActive }).SingleAsync(ct);
        var sizes = await db.FgSizes.AsNoTracking()
            .Where(s => s.FgId == fgId)
            .Join(db.Sizes, s => s.SizeId, z => z.Id, (s, z) => new { z.Id, z.Code, z.SortOrder })
            .OrderBy(z => z.SortOrder)
            .ToListAsync(ct);
        var steps = await db.FgRoutingSteps.AsNoTracking()
            .Where(s => s.FgId == fgId)
            .Join(db.Processes, s => s.ProcessId, p => p.Id, (s, p) => new { s.Seq, p.Id, p.Code, p.Name, p.Type, p.IsActive })
            .OrderBy(s => s.Seq)
            .ToListAsync(ct);
        var tariffKeys = await db.FgTariffs.AsNoTracking()
            .Where(t => t.FgId == fgId)
            .Select(t => new { t.ProcessId, t.SizeId })
            .ToListAsync(ct);
        var filled = tariffKeys.Select(t => (t.ProcessId, t.SizeId)).ToHashSet();

        var items = new List<ReadinessItem>
        {
            new("fg.active", fg.IsActive, fg.IsActive ? "FG aktif." : "FG nonaktif.", "general"),
            new("fg.sizes", sizes.Count > 0,
                sizes.Count > 0 ? $"{sizes.Count} Size Berlaku: {string.Join(", ", sizes.Select(s => s.Code))}." : "Belum ada Size Berlaku.",
                "sizes"),
        };

        var inactive = steps.Where(s => !s.IsActive).ToList();
        if (steps.Count == 0)
        {
            items.Add(new ReadinessItem("fg.routing", false, "Routing belum disusun.", "routing"));
        }
        else if (inactive.Count > 0)
        {
            items.Add(new ReadinessItem("fg.routing", false,
                $"Proses nonaktif pada routing: {string.Join(", ", inactive.Select(s => s.Code))}.", "routing"));
        }
        else
        {
            items.Add(new ReadinessItem("fg.routing", true, $"Routing memiliki {steps.Count} langkah, semua proses aktif.", "routing"));
        }

        var internalSteps = steps.Where(s => s.Type == ProcessType.Internal).ToList();
        if (steps.Count == 0 || sizes.Count == 0)
        {
            items.Add(new ReadinessItem("fg.tariffs", false, "Tarif dievaluasi setelah routing dan Size Berlaku tersedia.", "tariffs"));
        }
        else
        {
            var missing = new List<string>();
            foreach (var step in internalSteps)
            {
                foreach (var size in sizes.Where(z => !filled.Contains((step.Id, z.Id))))
                {
                    missing.Add($"Tarif {step.Name} untuk size {size.Code} belum diisi.");
                }
            }

            if (missing.Count == 0)
            {
                items.Add(new ReadinessItem("fg.tariffs", true,
                    internalSteps.Count == 0
                        ? "Tidak ada proses Internal yang membutuhkan tarif."
                        : $"Semua tarif terisi ({internalSteps.Count * sizes.Count} sel).",
                    "tariffs"));
            }
            else
            {
                foreach (var message in missing.Take(50))
                {
                    items.Add(new ReadinessItem("fg.tariffs", false, message, "tariffs"));
                }
            }
        }

        return (items.All(i => i.Ok), items);
    }
}
