using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Sales;

/// <summary>Validasi bersama pembuatan dan revisi PO (FSD Customer PO 4.1 dan 4.2).</summary>
internal static class PoValidation
{
    public const int MaxQty = 1_000_000;

    public static string? NormalizeCustomerPoNumber(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    /// <summary>Header: customer aktif, tanggal PO, nomor PO customer (≤100, unik per customer di antara PO tidak dibatalkan), keterangan.</summary>
    public static async Task ValidateHeaderAsync(
        GmsDbContext db, SavePoRequest request, PurchaseOrder? existing, bool headerLocked, ErrorBag errors, CancellationToken ct)
    {
        if (headerLocked && existing is not null)
        {
            if (request.CustomerId != existing.CustomerId)
            {
                errors.Add("customerId", "Customer tidak dapat diubah setelah PO dikonfirmasi.");
            }

            if (request.PoDate != existing.PoDate)
            {
                errors.Add("poDate", "Tanggal PO tidak dapat diubah setelah PO dikonfirmasi.");
            }
        }
        else if (existing is null || request.CustomerId != existing.CustomerId)
        {
            if (!await db.Customers.AnyAsync(c => c.Id == request.CustomerId && c.IsActive, ct))
            {
                errors.Add("customerId", "Customer tidak ditemukan atau nonaktif.");
            }
        }

        if (request.PoDate == default)
        {
            errors.Add("poDate", "Tanggal PO wajib diisi.");
        }

        var number = NormalizeCustomerPoNumber(request.CustomerPoNumber);
        if (number is { Length: > 100 })
        {
            errors.Add("customerPoNumber", "Nomor PO customer maksimal 100 karakter.");
        }

        if (request.Notes is { Length: > 1000 })
        {
            errors.Add("notes", "Keterangan maksimal 1000 karakter.");
        }
    }

    /// <summary>Nomor PO customer harus unik per customer di antara PO yang tidak dibatalkan (AC-PO-03).</summary>
    public static async Task EnsureCustomerPoNumberUniqueAsync(
        GmsDbContext db, Guid customerId, string? number, Guid? excludePoId, CancellationToken ct)
    {
        if (number is null)
        {
            return;
        }

        var clash = await db.PurchaseOrders.AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.CustomerPoNumber == number && p.Status != PoStatus.Cancelled
                        && (excludePoId == null || p.Id != excludePoId))
            .Select(p => p.Code).FirstOrDefaultAsync(ct);
        if (clash is not null)
        {
            throw ProblemException.Duplicate("customerPoNumber", $"Nomor PO customer ini sudah dipakai oleh {clash} untuk customer yang sama.");
        }
    }

    /// <summary>
    /// Baris: qty 1..1.000.000, tanggal kirim wajib, FG/Color/Size aktif dan Size termasuk Size Berlaku FG (untuk baris baru
    /// atau yang diubah), tanpa duplikat FG+Color+Size+Tanggal Kirim.
    /// </summary>
    public static async Task ValidateLinesAsync(
        GmsDbContext db, IReadOnlyList<SavePoLine> lines, IReadOnlyDictionary<Guid, PoLine> existingLines, ErrorBag errors, CancellationToken ct)
    {
        var fgIds = lines.Select(l => l.FgId).Distinct().ToList();
        var colorIds = lines.Select(l => l.ColorId).Distinct().ToList();
        var sizeIds = lines.Select(l => l.SizeId).Distinct().ToList();

        var fgs = await db.Fgs.AsNoTracking().Where(f => fgIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.IsActive, ct);
        var colors = await db.Colors.AsNoTracking().Where(c => colorIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.IsActive, ct);
        var sizes = await db.Sizes.AsNoTracking().Where(s => sizeIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.IsActive, ct);
        var fgSizeRows = await db.FgSizes.AsNoTracking().Where(s => fgIds.Contains(s.FgId)).Select(s => new { s.FgId, s.SizeId }).ToListAsync(ct);
        var fgSizes = fgSizeRows.Select(s => (s.FgId, s.SizeId)).ToHashSet();

        var seen = new HashSet<(Guid, Guid, Guid, DateOnly)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}]";

            if (line.Qty < 1)
            {
                errors.Add($"{prefix}.qty", "Qty harus lebih dari 0.");
            }
            else if (line.Qty > MaxQty)
            {
                errors.Add($"{prefix}.qty", $"Qty maksimal {MaxQty:N0}.");
            }

            if (line.DeliveryDate == default)
            {
                errors.Add($"{prefix}.deliveryDate", "Tanggal kirim wajib diisi.");
            }

            if (line.Notes is { Length: > 500 })
            {
                errors.Add($"{prefix}.notes", "Keterangan maksimal 500 karakter.");
            }

            if (!seen.Add((line.FgId, line.ColorId, line.SizeId, line.DeliveryDate)))
            {
                errors.Add($"{prefix}.fgId", "Kombinasi FG, Color, Size, dan Tanggal Kirim ganda dalam PO ini.");
            }

            var unchanged = line.Id is { } id && existingLines.TryGetValue(id, out var current)
                            && current.FgId == line.FgId && current.ColorId == line.ColorId && current.SizeId == line.SizeId;
            if (unchanged)
            {
                continue; // baris lama yang tidak berubah tidak divalidasi ulang terhadap master (PO adalah snapshot, E5/E6)
            }

            if (!fgs.TryGetValue(line.FgId, out var fgActive) || !fgActive)
            {
                errors.Add($"{prefix}.fgId", "FG tidak ditemukan atau nonaktif.");
            }

            if (!colors.TryGetValue(line.ColorId, out var colorActive) || !colorActive)
            {
                errors.Add($"{prefix}.colorId", "Color tidak ditemukan atau nonaktif.");
            }

            if (!sizes.TryGetValue(line.SizeId, out var sizeActive) || !sizeActive)
            {
                errors.Add($"{prefix}.sizeId", "Size tidak ditemukan atau nonaktif.");
            }
            else if (fgActive && !fgSizes.Contains((line.FgId, line.SizeId)))
            {
                errors.Add($"{prefix}.sizeId", "Size bukan Size Berlaku untuk FG ini.");
            }
        }
    }
}

/// <summary>Membuat PO baru (Draft), opsional langsung dikonfirmasi. Nomor PO customer boleh kosong (komitmen tanpa PO terbit).</summary>
public sealed class CreatePurchaseOrderHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, NumberSequences sequences, TimeProvider time, PoQueries queries)
{
    public async Task<PoDto> HandleAsync(SavePoRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        await PoValidation.ValidateHeaderAsync(db, request, null, false, errors, ct);

        var lines = request.Lines ?? [];
        await PoValidation.ValidateLinesAsync(db, lines, new Dictionary<Guid, PoLine>(), errors, ct);
        if (request.ConfirmAfterSave && lines.Count == 0)
        {
            errors.Add("lines", "PO tidak dapat dikonfirmasi tanpa baris.");
        }

        errors.ThrowIfAny();

        var number = PoValidation.NormalizeCustomerPoNumber(request.CustomerPoNumber);
        await PoValidation.EnsureCustomerPoNumberUniqueAsync(db, request.CustomerId, number, null, ct);

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var po = new PurchaseOrder
        {
            Code = await sequences.NextMonthlyCodeAsync("PO", ct),
            CustomerId = request.CustomerId,
            CustomerPoNumber = number,
            PoDate = request.PoDate,
            Status = request.ConfirmAfterSave ? PoStatus.Confirmed : PoStatus.Draft,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = now,
            CreatedBy = user.Id,
        };
        if (request.ConfirmAfterSave)
        {
            po.ConfirmedAt = now;
            po.ConfirmedBy = user.Id;
        }

        foreach (var line in lines)
        {
            po.Lines.Add(new PoLine
            {
                PoId = po.Id, FgId = line.FgId, ColorId = line.ColorId, SizeId = line.SizeId,
                DeliveryDate = line.DeliveryDate, Qty = line.Qty,
                Notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim(),
                CreatedAt = now, CreatedBy = user.Id,
            });
        }

        db.PurchaseOrders.Add(po);
        audit.Add(AuditEvents.PoCreated, "PurchaseOrder", po.Id.ToString(), null,
            new { po.Code, request.CustomerId, number, lines = lines.Count, status = po.Status.ToString() });
        if (request.ConfirmAfterSave)
        {
            audit.Add(AuditEvents.PoConfirmed, "PurchaseOrder", po.Id.ToString());
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await queries.GetDetailAsync(po.Id, ct);
    }
}

/// <summary>
/// Revisi PO. Draft: bebas. Confirmed/In Progress (FSD AC-PO-19 s.d. AC-PO-24): Customer dan Tanggal PO terkunci; Qty tidak boleh
/// turun di bawah Terkirim+Dialokasikan+Dalam Produksi; FG/Color/Size baris yang sudah punya alokasi/WO/pengiriman terkunci;
/// baris hanya dapat dihapus bila belum punya komitmen apa pun; PO otomatis Completed bila semua baris terkirim penuh.
/// Semua baris PO dikunci (FOR UPDATE, urut id) selama revisi sehingga alokasi/pengiriman bersamaan tidak menyelinap.
/// </summary>
public sealed class UpdatePurchaseOrderHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, PoQueries queries)
{
    public async Task<PoDto> HandleAsync(Guid id, SavePoRequest request, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ProblemException.NotFound("PO");
        if (po.Status is PoStatus.Completed or PoStatus.Cancelled)
        {
            throw ProblemException.StateConflict(
                po.Status == PoStatus.Completed
                    ? "PO sudah selesai dan terkunci. Tambahan pesanan dibuat pada PO baru."
                    : "PO yang dibatalkan tidak dapat diubah.");
        }

        db.Entry(po).Property(p => p.RowVersion).OriginalValue = request.RowVersion;
        var isDraft = po.Status == PoStatus.Draft;
        var requestLines = request.Lines ?? [];

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Kunci dulu, baru muat baris: pembacaan sesudah kunci melihat nilai terbaru (ADR-004).
        var lineIds = await db.PoLines.AsNoTracking().Where(l => l.PoId == id).Select(l => l.Id).ToListAsync(ct);
        await RowLocks.LockPoLinesAsync(db, lineIds, ct);
        var existing = await db.PoLines.Where(l => l.PoId == id).ToDictionaryAsync(l => l.Id, ct);
        var withHistory = await LinesWithHistoryAsync(existing.Keys.ToList(), ct);

        var errors = new ErrorBag();
        await PoValidation.ValidateHeaderAsync(db, request, po, !isDraft, errors, ct);
        await PoValidation.ValidateLinesAsync(db, requestLines, existing, errors, ct);
        if (!isDraft && requestLines.Count == 0)
        {
            errors.Add("lines", "PO yang sudah dikonfirmasi harus memiliki minimal 1 baris.");
        }

        var violations = new List<RuleViolation>();
        var keptIds = new HashSet<Guid>();
        for (var i = 0; i < requestLines.Count; i++)
        {
            var line = requestLines[i];
            if (line.Id is not { } lineId)
            {
                continue;
            }

            if (!existing.TryGetValue(lineId, out var current))
            {
                errors.Add($"lines[{i}].id", "Baris tidak ditemukan pada PO ini.");
                continue;
            }

            keptIds.Add(lineId);
            var committed = current.DeliveredQty + current.AllocatedQty + current.InProductionQty;
            var locked = committed > 0 || withHistory.Contains(lineId);

            if (locked && (current.FgId != line.FgId || current.ColorId != line.ColorId || current.SizeId != line.SizeId))
            {
                errors.Add($"lines[{i}].fgId", "FG, Color, dan Size tidak dapat diubah karena baris sudah memiliki alokasi, WO, atau pengiriman. Gunakan baris baru.");
            }

            if (line.Qty >= 1 && line.Qty < committed)
            {
                violations.Add(new RuleViolation(ProblemCodes.PoQtyBelowCommitted, $"Baris {i + 1}", line.Qty, committed,
                    $"Qty minimal {committed} (Terkirim {current.DeliveredQty} + Dialokasikan {current.AllocatedQty} + Dalam Produksi {current.InProductionQty}). " +
                    "Lepas alokasi atau ubah WO terlebih dahulu."));
            }
        }

        errors.ThrowIfAny();

        if (violations.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.PoQtyBelowCommitted, violations[0].Message, violations);
        }

        // Baris yang dihapus: hanya bila belum memiliki komitmen atau riwayat apa pun (AC-PO-22).
        var removed = existing.Values.Where(l => !keptIds.Contains(l.Id)).ToList();
        var blockedRemovals = removed
            .Where(l => l.DeliveredQty + l.AllocatedQty + l.InProductionQty > 0 || withHistory.Contains(l.Id))
            .Select(l => new RuleViolation(ProblemCodes.PoLineInUse, $"Baris {l.Id}", null, null,
                "Baris sudah memiliki alokasi, WO, atau pengiriman sehingga tidak dapat dihapus. Turunkan Qty bila perlu."))
            .ToList();
        if (blockedRemovals.Count > 0)
        {
            throw ProblemException.Rule(ProblemCodes.PoLineInUse, blockedRemovals[0].Message, blockedRemovals);
        }

        var number = PoValidation.NormalizeCustomerPoNumber(request.CustomerPoNumber);
        await PoValidation.EnsureCustomerPoNumberUniqueAsync(db, request.CustomerId, number, id, ct);

        var now = time.GetUtcNow();
        var before = new { po.CustomerPoNumber, po.Notes, lines = existing.Count };

        po.CustomerId = request.CustomerId;
        po.PoDate = request.PoDate;
        po.CustomerPoNumber = number;
        po.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        po.UpdatedAt = now;
        po.UpdatedBy = user.Id;

        foreach (var line in requestLines)
        {
            if (line.Id is { } lineId)
            {
                var current = existing[lineId];
                current.FgId = line.FgId;
                current.ColorId = line.ColorId;
                current.SizeId = line.SizeId;
                current.DeliveryDate = line.DeliveryDate;
                current.Qty = line.Qty;
                current.Notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim();
                current.UpdatedAt = now;
                current.UpdatedBy = user.Id;
            }
            else
            {
                db.PoLines.Add(new PoLine
                {
                    PoId = id, FgId = line.FgId, ColorId = line.ColorId, SizeId = line.SizeId,
                    DeliveryDate = line.DeliveryDate, Qty = line.Qty,
                    Notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim(),
                    CreatedAt = now, CreatedBy = user.Id,
                });
            }
        }

        db.PoLines.RemoveRange(removed);

        // Menurunkan Qty hingga semua baris terkirim penuh menutup PO secara otomatis (AC-PO-24).
        var completes = !isDraft && requestLines.Count > 0
                        && requestLines.All(l => l.Id is { } lid && existing.TryGetValue(lid, out var c) && c.DeliveredQty >= l.Qty);
        if (completes)
        {
            po.Status = PoStatus.Completed;
        }

        audit.Add(AuditEvents.PoUpdated, "PurchaseOrder", id.ToString(), before,
            new { po.CustomerPoNumber, po.Notes, lines = requestLines.Count, status = po.Status.ToString() });
        if (completes)
        {
            audit.Add(AuditEvents.PoStatusChanged, "PurchaseOrder", id.ToString(), new { status = "InProgress" }, new { status = "Completed" });
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await queries.GetDetailAsync(id, ct);
    }

    private async Task<HashSet<Guid>> LinesWithHistoryAsync(List<Guid> ids, CancellationToken ct)
    {
        var allocations = await db.PoAllocations.AsNoTracking().Where(a => ids.Contains(a.PoLineId)).Select(a => a.PoLineId).Distinct().ToListAsync(ct);
        var workOrders = await db.WoLines.AsNoTracking().Where(w => ids.Contains(w.PoLineId)).Select(w => w.PoLineId).Distinct().ToListAsync(ct);
        var deliveries = await db.DeliveryLines.AsNoTracking().Where(d => ids.Contains(d.PoLineId)).Select(d => d.PoLineId).Distinct().ToListAsync(ct);
        return allocations.Concat(workOrders).Concat(deliveries).ToHashSet();
    }
}
