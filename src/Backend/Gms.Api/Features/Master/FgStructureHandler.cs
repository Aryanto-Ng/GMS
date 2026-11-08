using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Domain.Master;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Master;

/// <summary>
/// Struktur FG: Size Berlaku, Routing, Tarif, kesiapan, dan salin dari FG lain (FSD Master Data 4.8 s.d. 4.11).
/// Setiap perubahan "menyentuh" baris FG sehingga dua penyunting bersamaan terdeteksi (xmin) dan seluruh perubahan
/// beserta nilai lama (tarif yang terhapus) tercatat di audit log.
/// </summary>
public sealed class FgStructureHandler(MasterContext ctx, FgHandler fgHandler)
{
    private const decimal MaxRate = 99_999_999.99999m;

    private GmsDbContext Db => ctx.Db;

    // ===== Size Berlaku =====

    public async Task<FgSizesDto> GetSizesAsync(Guid fgId, CancellationToken ct)
    {
        var rowVersion = await RowVersionAsync(fgId, ct);
        return new FgSizesDto(await SizeIdsAsync(fgId, ct), rowVersion);
    }

    public async Task<FgSizesDto> SaveSizesAsync(Guid fgId, SaveFgSizesRequest request, CancellationToken ct)
    {
        var fg = await LoadTrackedAsync(fgId, request.RowVersion, ct);
        var errors = new ErrorBag();
        var wanted = (request.SizeIds ?? []).Distinct().ToList();

        if (wanted.Count == 0)
        {
            errors.Add("sizeIds", "FG wajib memiliki minimal 1 Size Berlaku.");
        }

        var known = await Db.Sizes.AsNoTracking().Where(z => wanted.Contains(z.Id))
            .Select(z => new { z.Id, z.Code, z.IsActive }).ToListAsync(ct);
        foreach (var unknown in wanted.Except(known.Select(z => z.Id)))
        {
            errors.Add("sizeIds", $"Size {unknown} tidak ditemukan.");
        }

        var current = await Db.FgSizes.Where(s => s.FgId == fgId).ToListAsync(ct);
        var currentIds = current.Select(s => s.SizeId).ToHashSet();
        foreach (var added in known.Where(z => !currentIds.Contains(z.Id) && !z.IsActive))
        {
            errors.Add("sizeIds", $"Size {added.Code} nonaktif dan tidak dapat ditambahkan.");
        }

        errors.ThrowIfAny();

        var toRemove = current.Where(s => !wanted.Contains(s.SizeId)).ToList();
        var removedIds = toRemove.Select(s => s.SizeId).ToList();
        var affected = await Db.FgTariffs.AsNoTracking()
            .Where(t => t.FgId == fgId && removedIds.Contains(t.SizeId))
            .Join(Db.Sizes, t => t.SizeId, z => z.Id, (t, z) => new { t.ProcessId, t.SizeId, SizeCode = z.Code, t.Rate })
            .ToListAsync(ct);

        if (affected.Count > 0 && !request.ConfirmRemoveTariffs)
        {
            var violations = affected.GroupBy(a => a.SizeCode)
                .Select(g => new RuleViolation(ProblemCodes.FgSizeHasTariffs, $"Size {g.Key}", g.Count(), null,
                    $"{g.Count()} tarif pada size {g.Key} akan ikut terhapus."))
                .ToList();
            throw ProblemException.Rule(ProblemCodes.FgSizeHasTariffs,
                "Size yang dihapus sudah memiliki tarif. Konfirmasi untuk menghapus size beserta tarifnya.", violations);
        }

        var oldCodes = await SizeCodesAsync(currentIds, ct);
        Db.FgSizes.RemoveRange(toRemove);
        foreach (var sizeId in wanted.Where(id => !currentIds.Contains(id)))
        {
            Db.FgSizes.Add(new FgSize { FgId = fgId, SizeId = sizeId });
        }

        Touch(fg);
        var removedTariffs = affected.Select(a => new { size = a.SizeCode, processId = a.ProcessId, rate = a.Rate }).ToList();
        ctx.Audit.Add(AuditEvents.FgSizesChanged, nameof(Fg), fgId.ToString(),
            new { sizes = oldCodes },
            new { sizes = await SizeCodesAsync(wanted.ToHashSet(), ct), removedTariffs });
        await Db.SaveChangesAsync(ct);

        return new FgSizesDto(await SizeIdsAsync(fgId, ct), fg.RowVersion);
    }

    // ===== Routing =====

    public async Task<FgRoutingDto> GetRoutingAsync(Guid fgId, CancellationToken ct)
    {
        var rowVersion = await RowVersionAsync(fgId, ct);
        return new FgRoutingDto(await StepsAsync(fgId, ct), rowVersion);
    }

    public async Task<FgRoutingDto> SaveRoutingAsync(Guid fgId, SaveRoutingRequest request, CancellationToken ct)
    {
        var fg = await LoadTrackedAsync(fgId, request.RowVersion, ct);
        var errors = new ErrorBag();
        var ordered = request.ProcessIds ?? [];

        if (ordered.Count == 0)
        {
            errors.Add("processIds", "Routing minimal 1 langkah.");
        }

        if (ordered.Distinct().Count() != ordered.Count)
        {
            errors.Add("processIds", "Satu proses hanya boleh muncul sekali dalam routing.");
        }

        var processes = await Db.Processes.AsNoTracking().Where(p => ordered.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);
        foreach (var unknown in ordered.Where(id => !processes.ContainsKey(id)).Distinct())
        {
            errors.Add("processIds", $"Proses {unknown} tidak ditemukan.");
        }

        var current = await Db.FgRoutingSteps.Where(s => s.FgId == fgId).ToListAsync(ct);
        var currentProcessIds = current.Select(s => s.ProcessId).ToHashSet();
        foreach (var added in ordered.Where(id => processes.TryGetValue(id, out var p) && !p.IsActive && !currentProcessIds.Contains(id)))
        {
            errors.Add("processIds", $"Proses {processes[added].Code} nonaktif dan tidak dapat ditambahkan.");
        }

        errors.ThrowIfAny();

        var removed = current.Where(s => !ordered.Contains(s.ProcessId)).ToList();
        var removedProcessIds = removed.Select(s => s.ProcessId).ToList();
        var affected = await Db.FgTariffs.AsNoTracking()
            .Where(t => t.FgId == fgId && removedProcessIds.Contains(t.ProcessId))
            .Join(Db.Processes, t => t.ProcessId, p => p.Id, (t, p) => new { ProcessCode = p.Code, t.SizeId, t.Rate })
            .ToListAsync(ct);

        if (affected.Count > 0 && !request.ConfirmRemoveTariffs)
        {
            var violations = affected.GroupBy(a => a.ProcessCode)
                .Select(g => new RuleViolation(ProblemCodes.FgStepHasTariffs, $"Proses {g.Key}", g.Count(), null,
                    $"{g.Count()} tarif pada langkah {g.Key} akan ikut terhapus."))
                .ToList();
            throw ProblemException.Rule(ProblemCodes.FgStepHasTariffs,
                "Langkah yang dihapus sudah memiliki tarif. Konfirmasi untuk menghapus langkah beserta tarifnya.", violations);
        }

        var oldOrder = await StepsAsync(fgId, ct);
        Db.FgRoutingSteps.RemoveRange(removed);

        var byProcess = current.ToDictionary(s => s.ProcessId);
        for (var index = 0; index < ordered.Count; index++)
        {
            var seq = index + 1;
            if (byProcess.TryGetValue(ordered[index], out var step))
            {
                step.Seq = seq;
            }
            else
            {
                Db.FgRoutingSteps.Add(new FgRoutingStep { FgId = fgId, ProcessId = ordered[index], Seq = seq });
            }
        }

        Touch(fg);
        ctx.Audit.Add(AuditEvents.FgRoutingChanged, nameof(Fg), fgId.ToString(),
            new { steps = oldOrder.Select(s => s.ProcessCode).ToList() },
            new
            {
                steps = ordered.Select(id => processes[id].Code).ToList(),
                removedTariffs = affected.Select(a => new { process = a.ProcessCode, a.SizeId, rate = a.Rate }).ToList(),
            });
        await Db.SaveChangesAsync(ct);

        return new FgRoutingDto(await StepsAsync(fgId, ct), fg.RowVersion);
    }

    // ===== Tarif =====

    public async Task<FgTariffGridDto> GetTariffsAsync(Guid fgId, CancellationToken ct) =>
        await BuildGridAsync(fgId, await RowVersionAsync(fgId, ct), ct);

    public async Task<FgTariffGridDto> SaveTariffsAsync(Guid fgId, SaveTariffsRequest request, CancellationToken ct)
    {
        var fg = await LoadTrackedAsync(fgId, request.RowVersion, ct);
        var steps = await StepsAsync(fgId, ct);
        var internalIds = steps.Where(s => s.ProcessType == ProcessType.Internal).Select(s => s.ProcessId).ToHashSet();
        var sizeRows = await Db.FgSizes.AsNoTracking().Where(s => s.FgId == fgId)
            .Join(Db.Sizes, s => s.SizeId, z => z.Id, (s, z) => new { z.Id, z.Code }).ToListAsync(ct);
        var sizeCodes = sizeRows.ToDictionary(z => z.Id, z => z.Code);
        var processCodes = steps.ToDictionary(s => s.ProcessId, s => s.ProcessCode);

        var errors = new ErrorBag();
        var cells = request.Cells ?? [];
        var seen = new HashSet<(Guid, Guid)>();
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var path = $"cells[{i}].rate";

            if (!internalIds.Contains(cell.ProcessId))
            {
                errors.Add($"cells[{i}].processId", "Tarif hanya untuk langkah bertipe Internal pada routing FG ini.");
                continue;
            }

            if (!sizeCodes.ContainsKey(cell.SizeId))
            {
                errors.Add($"cells[{i}].sizeId", "Size bukan Size Berlaku FG ini.");
                continue;
            }

            if (!seen.Add((cell.ProcessId, cell.SizeId)))
            {
                errors.Add(path, "Sel tarif ganda pada permintaan.");
                continue;
            }

            if (cell.Rate is { } rate)
            {
                if (rate <= 0)
                {
                    errors.Add(path, "Tarif harus lebih dari 0.");
                }
                else if (decimal.Round(rate, 5) != rate)
                {
                    errors.Add(path, "Tarif maksimal 5 digit di belakang koma.");
                }
                else if (rate > MaxRate)
                {
                    errors.Add(path, "Tarif terlalu besar.");
                }
            }
        }

        errors.ThrowIfAny();

        var existing = await Db.FgTariffs.Where(t => t.FgId == fgId).ToListAsync(ct);
        var byKey = existing.ToDictionary(t => (t.ProcessId, t.SizeId));
        var changes = new List<object>();

        foreach (var cell in cells)
        {
            byKey.TryGetValue((cell.ProcessId, cell.SizeId), out var row);
            if (cell.Rate is { } rate)
            {
                if (row is null)
                {
                    Db.FgTariffs.Add(new FgTariff { FgId = fgId, ProcessId = cell.ProcessId, SizeId = cell.SizeId, Rate = rate });
                    changes.Add(new { process = processCodes[cell.ProcessId], size = sizeCodes[cell.SizeId], old = (decimal?)null, @new = (decimal?)rate });
                }
                else if (row.Rate != rate)
                {
                    changes.Add(new { process = processCodes[cell.ProcessId], size = sizeCodes[cell.SizeId], old = (decimal?)row.Rate, @new = (decimal?)rate });
                    row.Rate = rate;
                }
            }
            else if (row is not null)
            {
                changes.Add(new { process = processCodes[cell.ProcessId], size = sizeCodes[cell.SizeId], old = (decimal?)row.Rate, @new = (decimal?)null });
                Db.FgTariffs.Remove(row);
            }
        }

        Touch(fg);
        if (changes.Count > 0)
        {
            ctx.Audit.Add(AuditEvents.FgTariffsChanged, nameof(Fg), fgId.ToString(), null, new { changes = changes.Take(500).ToList(), total = changes.Count });
        }

        await Db.SaveChangesAsync(ct);
        return await BuildGridAsync(fgId, fg.RowVersion, ct);
    }

    // ===== Kesiapan =====

    public async Task<FgReadinessDto> GetReadinessAsync(Guid fgId, CancellationToken ct)
    {
        await RowVersionAsync(fgId, ct);
        var (ready, items) = await FgReadiness.EvaluateAsync(Db, fgId, ct);
        return new FgReadinessDto(ready, items);
    }

    // ===== Salin dari FG lain =====

    public async Task<CopyFgPreviewDto> PreviewCopyAsync(Guid fgId, CopyFgRequest request, CancellationToken ct)
    {
        await RowVersionAsync(fgId, ct);
        if (request.SourceFgId == fgId)
        {
            throw ProblemException.Validation("sourceFgId", "FG sumber harus berbeda dengan FG tujuan.");
        }

        if (!await Db.Fgs.AnyAsync(f => f.Id == request.SourceFgId, ct))
        {
            throw ProblemException.Validation("sourceFgId", "FG sumber tidak ditemukan.");
        }

        var sourceSteps = await StepsAsync(request.SourceFgId, ct);
        if (sourceSteps.Count == 0)
        {
            throw ProblemException.Validation("sourceFgId", "FG sumber belum memiliki routing.");
        }

        var inactive = await Db.Processes.AsNoTracking()
            .Where(p => sourceSteps.Select(s => s.ProcessId).Contains(p.Id) && !p.IsActive)
            .Select(p => p.Code).ToListAsync(ct);
        if (inactive.Count > 0)
        {
            throw ProblemException.Validation("sourceFgId", $"Routing sumber memuat proses nonaktif: {string.Join(", ", inactive)}.");
        }

        var targetSizeIds = await SizeIdsAsync(fgId, ct);
        var sourceTariffs = await Db.FgTariffs.AsNoTracking().Where(t => t.FgId == request.SourceFgId)
            .Join(Db.Sizes, t => t.SizeId, z => z.Id, (t, z) => new { t.ProcessId, t.SizeId, t.Rate, z.Code })
            .ToListAsync(ct);

        var cells = sourceTariffs.Where(t => targetSizeIds.Contains(t.SizeId))
            .Select(t => new TariffCell(t.ProcessId, t.SizeId, t.Rate)).ToList();
        var skipped = sourceTariffs.Where(t => !targetSizeIds.Contains(t.SizeId)).Select(t => t.Code).Distinct().Order().ToList();
        var willOverwrite = await Db.FgRoutingSteps.AnyAsync(s => s.FgId == fgId, ct)
                            || await Db.FgTariffs.AnyAsync(t => t.FgId == fgId, ct);

        return new CopyFgPreviewDto(sourceSteps, cells, skipped, willOverwrite);
    }

    public async Task<FgDto> CopyFromAsync(Guid fgId, CopyFgRequest request, CancellationToken ct)
    {
        var preview = await PreviewCopyAsync(fgId, request, ct);
        if (preview.WillOverwriteExisting && !request.ConfirmOverwrite)
        {
            throw ProblemException.Rule(ProblemCodes.FgCopyNeedsConfirm,
                "FG tujuan sudah memiliki routing/tarif. Konfirmasi untuk menimpanya.");
        }

        var fg = await Db.Fgs.SingleAsync(f => f.Id == fgId, ct);
        var oldSteps = await StepsAsync(fgId, ct);

        await using var tx = await Db.Database.BeginTransactionAsync(ct);

        // Hapus lebih dulu (di luar change tracker) agar tidak bentrok dengan kunci unik (fg_id, process_id).
        await Db.FgTariffs.Where(t => t.FgId == fgId).ExecuteDeleteAsync(ct);
        await Db.FgRoutingSteps.Where(s => s.FgId == fgId).ExecuteDeleteAsync(ct);

        foreach (var step in preview.Steps)
        {
            Db.FgRoutingSteps.Add(new FgRoutingStep { FgId = fgId, ProcessId = step.ProcessId, Seq = step.Seq });
        }

        var internalIds = preview.Steps.Where(s => s.ProcessType == ProcessType.Internal).Select(s => s.ProcessId).ToHashSet();
        foreach (var cell in preview.Cells.Where(c => c.Rate is not null && internalIds.Contains(c.ProcessId)))
        {
            Db.FgTariffs.Add(new FgTariff { FgId = fgId, ProcessId = cell.ProcessId, SizeId = cell.SizeId, Rate = cell.Rate!.Value });
        }

        Touch(fg);
        ctx.Audit.Add(AuditEvents.FgCopied, nameof(Fg), fgId.ToString(),
            new { steps = oldSteps.Select(s => s.ProcessCode).ToList() },
            new { sourceFgId = request.SourceFgId, steps = preview.Steps.Select(s => s.ProcessCode).ToList(), tariffs = preview.Cells.Count, skippedSizes = preview.SkippedSizeCodes });
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await fgHandler.GetAsync(fgId, ct);
    }

    // ===== Util =====

    private async Task<Fg> LoadTrackedAsync(Guid fgId, uint rowVersion, CancellationToken ct)
    {
        var fg = await Db.Fgs.SingleOrDefaultAsync(f => f.Id == fgId, ct) ?? throw ProblemException.NotFound("FG");
        Db.Entry(fg).Property(f => f.RowVersion).OriginalValue = rowVersion;
        return fg;
    }

    private void Touch(Fg fg)
    {
        fg.UpdatedAt = ctx.Time.GetUtcNow();
        fg.UpdatedBy = ctx.User.Id;
    }

    private async Task<uint> RowVersionAsync(Guid fgId, CancellationToken ct) =>
        await Db.Fgs.AsNoTracking().Where(f => f.Id == fgId).Select(f => (uint?)f.RowVersion).SingleOrDefaultAsync(ct)
        ?? throw ProblemException.NotFound("FG");

    private async Task<List<Guid>> SizeIdsAsync(Guid fgId, CancellationToken ct) =>
        await Db.FgSizes.AsNoTracking().Where(s => s.FgId == fgId)
            .Join(Db.Sizes, s => s.SizeId, z => z.Id, (s, z) => new { s.SizeId, z.SortOrder, z.Code })
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code)
            .Select(z => z.SizeId).ToListAsync(ct);

    private async Task<List<string>> SizeCodesAsync(HashSet<Guid> ids, CancellationToken ct) =>
        await Db.Sizes.AsNoTracking().Where(z => ids.Contains(z.Id)).OrderBy(z => z.SortOrder).Select(z => z.Code).ToListAsync(ct);

    private async Task<List<RoutingStepDto>> StepsAsync(Guid fgId, CancellationToken ct) =>
        // Urutkan SEBELUM proyeksi ke DTO: EF tidak dapat menerjemahkan OrderBy pada properti hasil konstruktor.
        await Db.FgRoutingSteps.AsNoTracking().Where(s => s.FgId == fgId)
            .Join(Db.Processes, s => s.ProcessId, p => p.Id, (s, p) => new { s.Seq, p.Id, p.Code, p.Name, p.Type })
            .OrderBy(x => x.Seq)
            .Select(x => new RoutingStepDto(x.Seq, x.Id, x.Code, x.Name, x.Type))
            .ToListAsync(ct);

    private async Task<FgTariffGridDto> BuildGridAsync(Guid fgId, uint rowVersion, CancellationToken ct)
    {
        var steps = await StepsAsync(fgId, ct);
        var sizes = await Db.FgSizes.AsNoTracking().Where(s => s.FgId == fgId)
            .Join(Db.Sizes, s => s.SizeId, z => z.Id, (s, z) => new { z.Id, z.Code, z.Name, z.SortOrder })
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code)
            .Select(z => new SizeRef(z.Id, z.Code, z.Name, z.SortOrder))
            .ToListAsync(ct);
        var rates = await Db.FgTariffs.AsNoTracking().Where(t => t.FgId == fgId)
            .ToDictionaryAsync(t => (t.ProcessId, t.SizeId), t => t.Rate, ct);

        var internalSteps = steps.Where(s => s.ProcessType == ProcessType.Internal).ToList();
        var cells = internalSteps
            .SelectMany(step => sizes.Select(size =>
                new TariffCell(step.ProcessId, size.Id, rates.TryGetValue((step.ProcessId, size.Id), out var r) ? r : null)))
            .ToList();

        return new FgTariffGridDto(internalSteps, steps.Where(s => s.ProcessType == ProcessType.Vendor).ToList(), sizes, cells, rowVersion);
    }
}
