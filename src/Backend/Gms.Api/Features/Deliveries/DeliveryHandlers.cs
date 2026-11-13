using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Deliveries;
using Gms.Domain.Sales;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Deliveries;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;
using Gms.Api.Features.Notifications;

namespace Gms.Api.Features.Deliveries;

internal static class DeliveryRules
{
    public static void ValidateHeader(DateOnly date, DateOnly today, string? recipient, string? vehicle, string? driver, string? notes, ErrorBag errors)
    {
        if (date > today)
        {
            errors.Add("deliveryDate", "Tanggal kirim tidak boleh di masa depan.");
        }

        if (recipient is { Length: > 150 }) { errors.Add("recipientName", "Nama penerima maksimal 150 karakter."); }
        if (vehicle is { Length: > 50 }) { errors.Add("vehicle", "Kendaraan maksimal 50 karakter."); }
        if (driver is { Length: > 100 }) { errors.Add("driver", "Sopir maksimal 100 karakter."); }
        if (notes is { Length: > 500 }) { errors.Add("notes", "Catatan maksimal 500 karakter."); }
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string RequireReason(string? reason, string aksi)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 500)
        {
            throw ProblemException.Validation("reason", $"Alasan {aksi} wajib diisi (maksimal 500 karakter).");
        }

        return text;
    }

    /// <summary>Validasi baris: qty 0..maks, tanpa duplikat, milik PO. Mengembalikan hanya baris dengan qty &gt; 0.</summary>
    public static List<(PoLine Line, int Qty)> ParseLines(IReadOnlyList<DeliveryLineQty>? lines, DeliveryState state, ErrorBag errors)
    {
        var result = new List<(PoLine, int)>();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < (lines?.Count ?? 0); i++)
        {
            var line = lines![i];
            if (!state.Lines.TryGetValue(line.PoLineId, out var poLine))
            {
                errors.Add($"lines[{i}].poLineId", "Baris tidak termasuk PO ini.");
                continue;
            }

            if (!seen.Add(line.PoLineId))
            {
                errors.Add($"lines[{i}].poLineId", "Baris yang sama muncul lebih dari sekali.");
                continue;
            }

            if (line.Qty is < 0 or > 1_000_000)
            {
                errors.Add($"lines[{i}].qty", "Qty harus bilangan bulat 0 sampai 1.000.000.");
                continue;
            }

            if (line.Qty > 0)
            {
                result.Add((poLine, line.Qty));
            }
        }

        errors.ThrowIfAny();
        if (result.Count == 0)
        {
            throw ProblemException.Validation("lines", "Minimal satu baris harus memiliki qty lebih dari 0.");
        }

        return result;
    }
}

/// <summary>PO yang masih memiliki kebutuhan tersisa (pemilih PO) dan konteks form per PO (AC-PG-01/02/04).</summary>
public sealed class DeliveryContextHandler(GmsDbContext db, AppTimeZone zone, TimeProvider time)
{
    public async Task<IReadOnlyList<EligiblePoItem>> ListEligibleAsync(string? q, CancellationToken ct)
    {
        var pos = db.PurchaseOrders.AsNoTracking().Where(p => p.Status == PoStatus.Confirmed || p.Status == PoStatus.InProgress);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = Paging.LikePattern(q.Trim());
            pos = pos.Where(p => EF.Functions.ILike(p.Code, pattern) || EF.Functions.ILike(p.CustomerPoNumber ?? "", pattern)
                                 || db.Customers.Any(c => c.Id == p.CustomerId && EF.Functions.ILike(c.Name, pattern)));
        }

        var rows = await pos.Select(p => new
        {
            p.Id, p.Code, p.CustomerPoNumber, p.PoDate,
            CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First(),
            Open = db.PoLines.Where(l => l.PoId == p.Id).Sum(l => (int?)(l.Qty - l.DeliveredQty)) ?? 0,
            Allocated = db.PoLines.Where(l => l.PoId == p.Id).Sum(l => (int?)l.AllocatedQty) ?? 0,
        }).Where(r => r.Open > 0).OrderByDescending(r => r.PoDate).ThenByDescending(r => r.Code).Take(100).ToListAsync(ct);
        return rows.Select(r => new EligiblePoItem(r.Id, r.Code, r.CustomerPoNumber, r.CustomerName, r.Open, r.Allocated)).ToList();
    }

    public async Task<DeliveryContextDto> GetAsync(Guid poId, CancellationToken ct)
    {
        var po = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == poId)
            .Select(p => new { p.Code, p.CustomerPoNumber, p.Status, CustomerName = db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).First() })
            .SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("PO");
        if (!DeliveryEngine.IsDeliverable(po.Status))
        {
            throw ProblemException.StateConflict($"Pengiriman hanya untuk PO berstatus Confirmed atau In Progress (status saat ini: {po.Status}).");
        }

        var today = zone.Today(time);
        var rows = await (
            from l in db.PoLines.AsNoTracking()
            where l.PoId == poId
            join f in db.Fgs on l.FgId equals f.Id
            join c in db.Colors on l.ColorId equals c.Id
            join s in db.Sizes on l.SizeId equals s.Id
            orderby f.Code, c.Code, s.SortOrder, l.DeliveryDate
            select new
            {
                Line = l, FgCode = f.Code, FgName = f.Name, ColorCode = c.Code, ColorName = c.Name, SizeCode = s.Code, SizeName = s.Name, s.SortOrder,
                Stock = db.StockBalances.Where(b => b.FgId == l.FgId && b.ColorId == l.ColorId && b.SizeId == l.SizeId)
                    .Select(b => b.OnHand - b.Allocated).FirstOrDefault(),
            }).ToListAsync(ct);

        var lines = rows.Select(r => new DeliveryLineOption(
            r.Line.Id, r.FgCode, r.FgName, new ColorSizeRef(r.Line.ColorId, r.ColorCode, r.ColorName, r.Line.SizeId, r.SizeCode, r.SizeName, r.SortOrder),
            r.Line.DeliveryDate, r.Line.Qty, r.Line.DeliveredQty, r.Line.AllocatedQty, DeliveryEngine.MaxDeliver(r.Line), Math.Max(r.Stock, 0),
            r.Line.DeliveryDate < today && r.Line.DeliveredQty < r.Line.Qty)).ToList();

        var warnings = rows
            .Where(r => r.Line.AllocatedQty < r.Line.Qty - r.Line.DeliveredQty)
            .Select(r => $"{r.FgCode} {r.ColorCode}/{r.SizeCode}: alokasi {r.Line.AllocatedQty} kurang dari sisa kebutuhan {r.Line.Qty - r.Line.DeliveredQty}.")
            .ToList();
        return new DeliveryContextDto(poId, po.Code, po.CustomerPoNumber, po.CustomerName, po.Status, lines, warnings);
    }
}

/// <summary>Surat jalan baru: ledger stok −, konsumsi alokasi, Terkirim +, status PO diperbarui, semuanya atomik (AC-PG-05/06).</summary>
public sealed class CreateDeliveryHandler(
    GmsDbContext db, DeliveryEngine engine, ICurrentUser user, IAuditWriter audit, TimeProvider time, NumberSequences sequences, DeliveryQueries queries)
{
    public async Task<DeliveryDto> HandleAsync(SaveDeliveryRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        DeliveryRules.ValidateHeader(request.DeliveryDate, engine.Today, request.RecipientName, request.Vehicle, request.Driver, request.Notes, errors);
        errors.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var state = await engine.OpenAsync(request.PoId, ct);
        if (!DeliveryEngine.IsDeliverable(state.PoStatus))
        {
            throw ProblemException.StateConflict($"Pengiriman hanya untuk PO berstatus Confirmed atau In Progress (status saat ini: {state.PoStatus}).");
        }

        var cells = DeliveryRules.ParseLines(request.Lines, state, errors);
        var now = time.GetUtcNow();
        var doc = new Delivery
        {
            Code = await sequences.NextMonthlyCodeAsync("SJ", ct), PoId = request.PoId, DeliveryDate = request.DeliveryDate,
            RecipientName = DeliveryRules.Clean(request.RecipientName), Vehicle = DeliveryRules.Clean(request.Vehicle),
            Driver = DeliveryRules.Clean(request.Driver), Notes = DeliveryRules.Clean(request.Notes), CreatedAt = now, CreatedBy = user.Id,
        };
        engine.Apply(state, doc, cells, user.Id, now);
        DeliveryEngine.ThrowIfAny(engine.CheckFinalState(state));
        db.Deliveries.Add(doc);
        audit.Add(AuditEvents.DeliveryCreated, "Delivery", doc.Id.ToString(), null,
            new { doc.Code, request.PoId, totalQty = cells.Sum(c => c.Qty), lines = cells.Count });
        await engine.SaveAsync(ct);

        var changed = await engine.RefreshPoStatusAsync(request.PoId, ct);
        if (changed is { } status)
        {
            audit.Add(AuditEvents.PoStatusChanged, "PurchaseOrder", request.PoId.ToString(), null, new { status = status.ToString(), source = doc.Code });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return await queries.GetAsync(doc.Id, ct);
    }
}

/// <summary>Hanya informasi non-qty yang dapat diubah (AC-PG-07/08).</summary>
public sealed class UpdateDeliveryInfoHandler(
    GmsDbContext db, DeliveryEngine engine, ICurrentUser user, IAuditWriter audit, TimeProvider time, DeliveryQueries queries)
{
    public async Task<DeliveryDto> HandleAsync(Guid id, UpdateDeliveryInfoRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        DeliveryRules.ValidateHeader(request.DeliveryDate, engine.Today, request.RecipientName, request.Vehicle, request.Driver, request.Notes, errors);
        errors.ThrowIfAny();

        var doc = await db.Deliveries.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw ProblemException.NotFound("Surat jalan");
        db.Entry(doc).Property(d => d.RowVersion).OriginalValue = request.RowVersion;
        if (doc.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Surat jalan yang sudah di-void tidak dapat diubah.");
        }

        var before = new { doc.DeliveryDate, doc.RecipientName, doc.Vehicle, doc.Driver, doc.Notes };
        doc.DeliveryDate = request.DeliveryDate;
        doc.RecipientName = DeliveryRules.Clean(request.RecipientName);
        doc.Vehicle = DeliveryRules.Clean(request.Vehicle);
        doc.Driver = DeliveryRules.Clean(request.Driver);
        doc.Notes = DeliveryRules.Clean(request.Notes);
        doc.UpdatedAt = time.GetUtcNow();
        doc.UpdatedBy = user.Id;
        audit.Add(AuditEvents.DeliveryInfoUpdated, "Delivery", id.ToString(), before,
            new { doc.DeliveryDate, doc.RecipientName, doc.Vehicle, doc.Driver, doc.Notes });
        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(id, ct);
    }
}

/// <summary>Void: semua dampak dibalik atomik; PO Completed yang tidak lagi terpenuhi kembali In Progress (AC-PG-09..11, E5/E6).</summary>
public sealed class VoidDeliveryHandler(
    GmsDbContext db, DeliveryEngine engine, ICurrentUser user, IAuditWriter audit, TimeProvider time, DeliveryQueries queries,
    NotificationEmitter notify)
{
    public async Task<ImpactPreview> PreviewAsync(Guid id, CancellationToken ct) => (await RunAsync(id, null, dryRun: true, ct)).Preview!;

    public async Task<DeliveryDto> HandleAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await RunAsync(id, DeliveryRules.RequireReason(request.Reason, "void"), dryRun: false, ct);
        return await queries.GetAsync(id, ct);
    }

    private async Task<(ImpactPreview? Preview, bool Done)> RunAsync(Guid id, string? reason, bool dryRun, CancellationToken ct)
    {
        var poId = await db.Deliveries.AsNoTracking().Where(d => d.Id == id).Select(d => (Guid?)d.PoId).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Surat jalan");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var state = await engine.OpenAsync(poId, ct);
        var doc = await db.Deliveries.Include(d => d.Lines).SingleAsync(d => d.Id == id, ct);
        if (doc.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Surat jalan sudah di-void dan tidak dapat diaktifkan kembali.");
        }

        if (state.PoStatus == PoStatus.Cancelled)
        {
            throw ProblemException.StateConflict("Void ditolak karena PO sudah dibatalkan. Data tidak konsisten; laporkan kepada administrator.");
        }

        engine.Reverse(state, doc, reason ?? "(pratinjau)", user.Id, time.GetUtcNow());
        var violations = engine.CheckFinalState(state);
        if (dryRun)
        {
            var notes = new List<string> { "Stok bertambah kembali, konsumsi alokasi dibalik, dan Terkirim pada baris PO berkurang." };
            if (state.PoStatus == PoStatus.Completed)
            {
                notes.Add("PO berstatus Completed akan kembali ke In Progress.");
            }

            var preview = DeliveryEngine.BuildImpact(state, violations, notes);
            await tx.RollbackAsync(ct);
            return (preview, false);
        }

        DeliveryEngine.ThrowIfAny(violations);
        await notify.DeliveryChangedAsync(doc, "void", ct);
        audit.Add(AuditEvents.DeliveryVoided, "Delivery", id.ToString(), new { status = "Active" }, new { status = "Voided", reason, doc.Code });
        await engine.SaveAsync(ct);

        var changed = await engine.RefreshPoStatusAsync(poId, ct);
        if (changed is { } status)
        {
            audit.Add(AuditEvents.PoStatusChanged, "PurchaseOrder", poId.ToString(), null, new { status = status.ToString(), source = doc.Code });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return (null, true);
    }
}

/// <summary>Koreksi = void lama + surat jalan baru atomik; batas dihitung pada kondisi akhir (AC-PG-12).</summary>
public sealed class CorrectDeliveryHandler(
    GmsDbContext db, DeliveryEngine engine, ICurrentUser user, IAuditWriter audit, TimeProvider time, NumberSequences sequences, DeliveryQueries queries,
    NotificationEmitter notify)
{
    public async Task<ImpactPreview> PreviewAsync(Guid id, CorrectDeliveryRequest request, CancellationToken ct) =>
        (await RunAsync(id, request, dryRun: true, ct)).Preview!;

    public async Task<DeliveryDto> HandleAsync(Guid id, CorrectDeliveryRequest request, CancellationToken ct)
    {
        var newId = (await RunAsync(id, request, dryRun: false, ct)).NewId;
        return await queries.GetAsync(newId, ct);
    }

    private async Task<(Guid NewId, ImpactPreview? Preview)> RunAsync(Guid id, CorrectDeliveryRequest request, bool dryRun, CancellationToken ct)
    {
        var reason = DeliveryRules.RequireReason(request.Reason, "koreksi");
        var errors = new ErrorBag();
        DeliveryRules.ValidateHeader(request.DeliveryDate, engine.Today, request.RecipientName, request.Vehicle, request.Driver, request.Notes, errors);
        errors.ThrowIfAny();

        var poId = await db.Deliveries.AsNoTracking().Where(d => d.Id == id).Select(d => (Guid?)d.PoId).SingleOrDefaultAsync(ct)
                   ?? throw ProblemException.NotFound("Surat jalan");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var state = await engine.OpenAsync(poId, ct);
        var old = await db.Deliveries.Include(d => d.Lines).SingleAsync(d => d.Id == id, ct);
        if (old.Status == DocStatus.Voided)
        {
            throw ProblemException.StateConflict("Surat jalan yang sudah di-void tidak dapat dikoreksi.");
        }

        if (state.PoStatus == PoStatus.Cancelled)
        {
            throw ProblemException.StateConflict("Koreksi ditolak karena PO sudah dibatalkan.");
        }

        var cells = DeliveryRules.ParseLines(request.Lines, state, errors);
        var now = time.GetUtcNow();
        engine.Reverse(state, old, $"Dikoreksi: {reason}", user.Id, now);
        var replacement = new Delivery
        {
            Code = dryRun ? "(pratinjau)" : await sequences.NextMonthlyCodeAsync("SJ", ct), PoId = poId, DeliveryDate = request.DeliveryDate,
            RecipientName = DeliveryRules.Clean(request.RecipientName), Vehicle = DeliveryRules.Clean(request.Vehicle),
            Driver = DeliveryRules.Clean(request.Driver), Notes = DeliveryRules.Clean(request.Notes), ReplacesId = old.Id,
            CreatedAt = now, CreatedBy = user.Id,
        };
        var violations = new List<RuleViolation>();
        try
        {
            engine.Apply(state, replacement, cells, user.Id, now);
        }
        catch (ProblemException ex) when (dryRun && ex.Violations is { Count: > 0 })
        {
            violations.AddRange(ex.Violations); // pratinjau menjelaskan pelanggaran alih-alih gagal
        }

        if (violations.Count == 0)
        {
            violations.AddRange(engine.CheckFinalState(state));
        }

        if (dryRun)
        {
            var preview = DeliveryEngine.BuildImpact(state, violations, ["Surat jalan lama di-void dan diganti surat jalan baru dalam satu transaksi."]);
            await tx.RollbackAsync(ct);
            return (Guid.Empty, preview);
        }

        DeliveryEngine.ThrowIfAny(violations);
        db.Deliveries.Add(replacement);
        await notify.DeliveryChangedAsync(old, "correct", ct);
        audit.Add(AuditEvents.DeliveryCorrected, "Delivery", id.ToString(), new { totalQty = old.Lines.Sum(l => l.Qty), old.Code },
            new { replacedBy = replacement.Id, replacement.Code, totalQty = cells.Sum(c => c.Qty), reason });
        await engine.SaveAsync(ct);

        var changed = await engine.RefreshPoStatusAsync(poId, ct);
        if (changed is { } status)
        {
            audit.Add(AuditEvents.PoStatusChanged, "PurchaseOrder", poId.ToString(), null, new { status = status.ToString(), source = replacement.Code });
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return (replacement.Id, null);
    }
}

/// <summary>
/// Cetak Surat Jalan PDF (AC-PG-17..23). <c>print_count</c> dinaikkan atomik dalam transaksi yang sama dengan pembuatan PDF dan audit,
/// sehingga pencetakan yang gagal tidak tercatat. Cetakan ke-2 dst. dari surat jalan aktif diberi "CETAK ULANG"; Voided ber-watermark.
/// </summary>
public sealed class GetDeliveryPdfHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, AppTimeZone zone,
    Microsoft.Extensions.Options.IOptions<Gms.Api.Security.AppOptions> options)
{
    public async Task<FileContent> HandleAsync(Guid id, CancellationToken ct)
    {
        var doc = await db.Deliveries.AsNoTracking().Include(d => d.Lines).SingleOrDefaultAsync(d => d.Id == id, ct)
                  ?? throw ProblemException.NotFound("Surat jalan");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE deliveries SET print_count = print_count + 1 WHERE id = {id}", ct);
        var printCount = await db.Deliveries.AsNoTracking().Where(d => d.Id == id).Select(d => d.PrintCount).SingleAsync(ct);

        var po = await db.PurchaseOrders.AsNoTracking().Where(p => p.Id == doc.PoId)
            .Select(p => new { p.Code, p.CustomerPoNumber, p.CustomerId }).SingleAsync(ct);
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == po.CustomerId).Select(c => new { c.Name, c.Address }).SingleAsync(ct);
        var lines = await (
            from l in db.DeliveryLines.AsNoTracking()
            where l.DeliveryId == id
            join p in db.PoLines on l.PoLineId equals p.Id
            join f in db.Fgs on p.FgId equals f.Id
            join c in db.Colors on p.ColorId equals c.Id
            join s in db.Sizes on p.SizeId equals s.Id
            select new Gms.Api.Documents.PrintLine(f.Code, f.Name, c.Code, c.Name, s.SortOrder, s.Code, l.Qty)).ToListAsync(ct);
        var printedBy = await db.Users.AsNoTracking().Where(u => u.Id == user.Id).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? user.UserName ?? "-";

        var voided = doc.Status == DocStatus.Voided;
        var model = new Gms.Api.Documents.DeliveryPrintModel(
            doc.Code, doc.DeliveryDate, po.Code, po.CustomerPoNumber, customer.Name, customer.Address, doc.RecipientName, doc.Vehicle, doc.Driver,
            doc.Notes, options.Value.Comfi.Name, options.Value.Comfi.Address, lines, voided, doc.VoidReason,
            IsReprint: !voided && printCount > 1, printedBy, zone.Now(time));
        var bytes = Gms.Api.Documents.DeliveryPdfBuilder.Build(model, options.Value.Pdf.FontDirectory);

        audit.Add(AuditEvents.DeliveryPrinted, "Delivery", id.ToString(), null, new { doc.Code, printCount, voided });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new FileContent($"{doc.Code}.pdf", "application/pdf", bytes);
    }
}
