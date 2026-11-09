using System.Globalization;
using System.Text;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Inventory;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Inventory;

/// <summary>Daftar stok FG (Fisik, Teralokasi, Tersedia) dan kartu stok dengan saldo berjalan (AC-ST-12/13/15).</summary>
public sealed class StockQueries(GmsDbContext db)
{
    // Proyeksi skalar (bukan DTO berkonstruktor) agar filter dan urutan dapat diterjemahkan EF.
    private sealed class S
    {
        public Guid FgId { get; set; }
        public string FgCode { get; set; } = string.Empty;
        public string FgName { get; set; } = string.Empty;
        public Guid ColorId { get; set; }
        public string ColorCode { get; set; } = string.Empty;
        public string ColorName { get; set; } = string.Empty;
        public Guid SizeId { get; set; }
        public string SizeCode { get; set; } = string.Empty;
        public string SizeName { get; set; } = string.Empty;
        public int SizeSort { get; set; }
        public int OnHand { get; set; }
        public int Allocated { get; set; }
    }

    private static StockRow ToRow(S s) =>
        new(s.FgId, s.FgCode, s.FgName, new ColorSizeRef(s.ColorId, s.ColorCode, s.ColorName, s.SizeId, s.SizeCode, s.SizeName, s.SizeSort),
            s.OnHand, s.Allocated, s.OnHand - s.Allocated);

    private IQueryable<S> Rows() =>
        from s in db.StockBalances.AsNoTracking()
        join f in db.Fgs.AsNoTracking() on s.FgId equals f.Id
        join c in db.Colors.AsNoTracking() on s.ColorId equals c.Id
        join z in db.Sizes.AsNoTracking() on s.SizeId equals z.Id
        select new S
        {
            FgId = f.Id, FgCode = f.Code, FgName = f.Name, ColorId = c.Id, ColorCode = c.Code, ColorName = c.Name,
            SizeId = z.Id, SizeCode = z.Code, SizeName = z.Name, SizeSort = z.SortOrder, OnHand = s.OnHand, Allocated = s.Allocated,
        };

    public async Task<StockListResult> ListAsync(StockQuery query, CancellationToken ct)
    {
        var rows = Rows();
        if (query.FgId is { } fgId) { rows = rows.Where(r => r.FgId == fgId); }
        if (query.ColorId is { } colorId) { rows = rows.Where(r => r.ColorId == colorId); }
        if (query.SizeId is { } sizeId) { rows = rows.Where(r => r.SizeId == sizeId); }
        if (query.CustomerId is { } customerId) { rows = rows.Where(r => db.Fgs.Any(f => f.Id == r.FgId && f.CustomerId == customerId)); }
        if (query.HanyaAdaStok) { rows = rows.Where(r => r.OnHand > 0); }
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            rows = rows.Where(r => EF.Functions.ILike(r.FgCode, pattern) || EF.Functions.ILike(r.FgName, pattern));
        }

        var onHand = await rows.SumAsync(r => (int?)r.OnHand, ct) ?? 0;
        var allocated = await rows.SumAsync(r => (int?)r.Allocated, ct) ?? 0;
        var total = await rows.CountAsync(ct);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var items = await rows.OrderBy(r => r.FgCode).ThenBy(r => r.ColorCode).ThenBy(r => r.SizeSort)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new StockListResult(new PagedResult<StockRow>(items.Select(ToRow).ToList(), page, pageSize, total), onHand, allocated, onHand - allocated);
    }

    public async Task<StockLedgerResult> GetLedgerAsync(StockLedgerQuery query, CancellationToken ct)
    {
        var found = await Rows().Where(r => r.FgId == query.FgId && r.ColorId == query.ColorId && r.SizeId == query.SizeId).SingleOrDefaultAsync(ct);
        var header = found is not null ? ToRow(found) : null;
        if (header is null)
        {
            // Kombinasi valid tanpa saldo: tampilkan nol. Kombinasi tidak dikenal: 404.
            var zero = await (
                from f in db.Fgs.AsNoTracking().Where(f => f.Id == query.FgId)
                from c in db.Colors.AsNoTracking().Where(c => c.Id == query.ColorId)
                from z in db.Sizes.AsNoTracking().Where(z => z.Id == query.SizeId)
                select new S
                {
                    FgId = f.Id, FgCode = f.Code, FgName = f.Name, ColorId = c.Id, ColorCode = c.Code, ColorName = c.Name,
                    SizeId = z.Id, SizeCode = z.Code, SizeName = z.Name, SizeSort = z.SortOrder,
                }).SingleOrDefaultAsync(ct) ?? throw ProblemException.NotFound("Stok");
            header = ToRow(zero);
        }

        var entries = db.StockLedger.AsNoTracking().Where(e => e.FgId == query.FgId && e.ColorId == query.ColorId && e.SizeId == query.SizeId);
        var filtered = entries;
        if (query.From is { } from) { filtered = filtered.Where(e => e.EntryDate >= from); }
        if (query.To is { } to) { filtered = filtered.Where(e => e.EntryDate <= to); }

        var total = await filtered.CountAsync(ct);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var pageEntries = await filtered.OrderByDescending(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        // Saldo berjalan = Σ qty seluruh entri dengan id ≤ entri ini (bukan hanya yang terfilter).
        var running = new Dictionary<long, int>();
        if (pageEntries.Count > 0)
        {
            var minId = pageEntries.Min(e => e.Id);
            var maxId = pageEntries.Max(e => e.Id);
            var balance = await entries.Where(e => e.Id < minId).SumAsync(e => (int?)e.Qty, ct) ?? 0;
            var inRange = await entries.Where(e => e.Id >= minId && e.Id <= maxId).OrderBy(e => e.Id).Select(e => new { e.Id, e.Qty }).ToListAsync(ct);
            foreach (var e in inRange)
            {
                balance += e.Qty;
                running[e.Id] = balance;
            }
        }

        var handoverIds = pageEntries.Where(e => e.SourceType == "FgHandover" && e.SourceId != null).Select(e => e.SourceId!.Value).Distinct().ToList();
        var labels = await (
            from h in db.FgHandovers.AsNoTracking().Where(h => handoverIds.Contains(h.Id))
            join w in db.WorkOrders on h.WoId equals w.Id
            select new { h.Id, Label = "Serah Terima " + w.Code + (h.DocNo != null ? " · " + h.DocNo : "") }).ToDictionaryAsync(x => x.Id, x => x.Label, ct);
        var userIds = pageEntries.Select(e => e.CreatedBy).Where(x => x != null).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var rows = pageEntries.Select(e => new StockLedgerRow(
            e.Id, e.EntryDate, e.Kind, e.SourceType, e.SourceId,
            e.SourceId is { } sid && labels.TryGetValue(sid, out var label) ? label : e.SourceType,
            e.Qty, running[e.Id], e.Reason, e.CreatedBy is { } uid ? users.GetValueOrDefault(uid) : null)).ToList();
        return new StockLedgerResult(header, new PagedResult<StockLedgerRow>(rows, page, pageSize, total));
    }
}

/// <summary>Penyesuaian stok manual dengan alasan wajib; tidak boleh membuat Stok Tersedia &lt; 0 (AC-ST-16..19).</summary>
public sealed class AdjustStockHandler(GmsDbContext db, AppTimeZone zone, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    public async Task<ImpactPreview> PreviewAsync(AdjustStockRequest request, CancellationToken ct) => (await RunAsync(request, dryRun: true, ct))!;

    public async Task HandleAsync(AdjustStockRequest request, CancellationToken ct) => await RunAsync(request, dryRun: false, ct);

    private async Task<ImpactPreview?> RunAsync(AdjustStockRequest request, bool dryRun, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        var errors = new ErrorBag();
        if (request.Qty == 0 || Math.Abs((long)request.Qty) > 1_000_000)
        {
            errors.Add("qty", "Qty harus bilangan bulat bukan nol (maksimal 1.000.000 per penyesuaian).");
        }

        if (request.Date > zone.Today(time))
        {
            errors.Add("date", "Tanggal tidak boleh di masa depan.");
        }

        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            errors.Add("reason", "Alasan wajib diisi (maksimal 500 karakter).");
        }

        errors.ThrowIfAny();

        await StockCombination.EnsureValidAsync(db, request.FgId, request.ColorId, request.SizeId, requireActive: false, "", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RowLocks.EnsureAndLockStockAsync(db, [(request.FgId, request.ColorId, request.SizeId)], ct);
        var stock = await db.StockBalances.SingleAsync(s => s.FgId == request.FgId && s.ColorId == request.ColorId && s.SizeId == request.SizeId, ct);

        var before = (stock.OnHand, stock.Allocated);
        var onHandAfter = stock.OnHand + request.Qty;
        var available = onHandAfter - stock.Allocated;
        var rows = new List<ImpactRow>
        {
            new("Stok", "Stok Fisik", before.OnHand, onHandAfter, null, onHandAfter >= 0, null),
            new("Stok", "Stok Tersedia", before.OnHand - before.Allocated, available, 0, available >= 0,
                available >= 0 ? null : $"Stok tersedia akan menjadi {available}. Lepas alokasi PO terlebih dahulu."),
        };
        if (dryRun)
        {
            await tx.RollbackAsync(ct);
            var violations = available >= 0 ? [] : new List<RuleViolation>
            {
                new(ProblemCodes.StockInsufficient, "Stok", available, 0, $"Stok tersedia akan menjadi {available}. Lepas alokasi PO terlebih dahulu."),
            };
            return new ImpactPreview(rows, available >= 0, violations, []);
        }

        if (available < 0)
        {
            var message = $"Penyesuaian membuat stok tersedia {available} (fisik {onHandAfter}, teralokasi {stock.Allocated}). Lepas alokasi PO terlebih dahulu.";
            throw ProblemException.Rule(ProblemCodes.StockInsufficient, message,
                [new RuleViolation(ProblemCodes.StockInsufficient, "Stok", available, 0, message)]);
        }

        stock.OnHand = onHandAfter;
        db.StockLedger.Add(new StockLedgerEntry
        {
            FgId = request.FgId, ColorId = request.ColorId, SizeId = request.SizeId, Kind = StockLedgerKind.Adjustment, Qty = request.Qty,
            SourceType = "Adjustment", EntryDate = request.Date, Reason = reason, CreatedAt = time.GetUtcNow(), CreatedBy = user.Id,
        });
        audit.Add(AuditEvents.StockAdjusted, "StockBalance", request.FgId.ToString(),
            new { request.ColorId, request.SizeId, onHand = before.OnHand }, new { request.ColorId, request.SizeId, onHand = onHandAfter, request.Qty, reason });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }
}

internal static class StockCombination
{
    /// <summary>FG, Color, dan Size harus ada, Size harus termasuk Size Berlaku FG. <paramref name="requireActive"/> untuk impor baru (E8).</summary>
    public static async Task EnsureValidAsync(
        GmsDbContext db, Guid fgId, Guid colorId, Guid sizeId, bool requireActive, string prefix, CancellationToken ct)
    {
        var fg = await db.Fgs.AsNoTracking().Where(f => f.Id == fgId).Select(f => new { f.IsActive, f.Code }).SingleOrDefaultAsync(ct);
        var color = await db.Colors.AsNoTracking().Where(c => c.Id == colorId).Select(c => new { c.IsActive, c.Code }).SingleOrDefaultAsync(ct);
        var size = await db.Sizes.AsNoTracking().Where(s => s.Id == sizeId).Select(s => new { s.IsActive, s.Code }).SingleOrDefaultAsync(ct);
        var errors = new ErrorBag();
        if (fg is null) { errors.Add(prefix + "fgId", "FG tidak ditemukan."); }
        else if (requireActive && !fg.IsActive) { errors.Add(prefix + "fgId", $"FG {fg.Code} nonaktif."); }
        if (color is null) { errors.Add(prefix + "colorId", "Color tidak ditemukan."); }
        else if (requireActive && !color.IsActive) { errors.Add(prefix + "colorId", $"Color {color.Code} nonaktif."); }
        if (size is null) { errors.Add(prefix + "sizeId", "Size tidak ditemukan."); }
        else if (requireActive && !size.IsActive) { errors.Add(prefix + "sizeId", $"Size {size.Code} nonaktif."); }
        errors.ThrowIfAny();

        if (!await db.FgSizes.AnyAsync(s => s.FgId == fgId && s.SizeId == sizeId, ct))
        {
            throw ProblemException.Validation(prefix + "sizeId", $"Size {size!.Code} bukan Size Berlaku untuk FG {fg!.Code}.");
        }
    }
}

/// <summary>Impor saldo awal CSV (AC-ST-20..22): semua-atau-tidak-sama-sekali, bersifat menambah, batas 2.000 baris / 2 MB.</summary>
public sealed class StockOpeningImportHandler(GmsDbContext db, AppTimeZone zone, ICurrentUser user, IAuditWriter audit, TimeProvider time)
{
    private static readonly string[] Columns = ["fg_code", "color_code", "size_code", "qty", "date"];

    public FileContent GetTemplate()
    {
        var csv = string.Join(',', Columns) + "\r\nKMJ-001,NAVY,M,120,\r\n";
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return new FileContent("template-saldo-awal-stok.csv", "text/csv", bytes);
    }

    public async Task<ImportResultDto> ImportAsync(Stream csv, bool dryRun, CancellationToken ct)
    {
        var table = await CsvParser.ParseAsync(csv, ct);
        var header = table.Header.Select(h => h.Trim().ToLowerInvariant()).ToList();
        if (header.Count != Columns.Length || Columns.Except(header).Any())
        {
            throw CsvParser.Invalid("Judul kolom tidak sesuai template (" + string.Join(", ", Columns) + "). Unduh template terbaru.");
        }

        var fgs = await db.Fgs.AsNoTracking().ToDictionaryAsync(f => f.Code.ToLower(), ct);
        var colors = await db.Colors.AsNoTracking().ToDictionaryAsync(c => c.Code.ToLower(), ct);
        var sizes = await db.Sizes.AsNoTracking().ToDictionaryAsync(s => s.Code.ToLower(), ct);
        var fgSizes = (await db.FgSizes.AsNoTracking().Select(s => new { s.FgId, s.SizeId }).ToListAsync(ct)).Select(s => (s.FgId, s.SizeId)).ToHashSet();
        var openings = (await db.StockLedger.AsNoTracking().Where(e => e.Kind == StockLedgerKind.OpeningBalance)
            .Select(e => new { e.FgId, e.ColorId, e.SizeId }).Distinct().ToListAsync(ct)).Select(e => new StockKey(e.FgId, e.ColorId, e.SizeId)).ToHashSet();
        var today = zone.Today(time);

        var results = new List<ImportRowResult>();
        var valid = new List<(StockKey Key, int Qty, DateOnly Date)>();
        foreach (var row in table.Rows)
        {
            string Cell(string name) => header.IndexOf(name) is var i and >= 0 && i < row.Cells.Count ? row.Cells[i].Trim() : string.Empty;
            var fgCode = Cell("fg_code");
            var colorCode = Cell("color_code");
            var sizeCode = Cell("size_code");
            var key = $"{fgCode}/{colorCode}/{sizeCode}";
            var problems = new List<string>();

            fgs.TryGetValue(fgCode.ToLower(), out var fg);
            colors.TryGetValue(colorCode.ToLower(), out var color);
            sizes.TryGetValue(sizeCode.ToLower(), out var size);
            if (fg is null) { problems.Add("FG tidak ditemukan."); } else if (!fg.IsActive) { problems.Add("FG nonaktif."); }
            if (color is null) { problems.Add("Color tidak ditemukan."); } else if (!color.IsActive) { problems.Add("Color nonaktif."); }
            if (size is null) { problems.Add("Size tidak ditemukan."); } else if (!size.IsActive) { problems.Add("Size nonaktif."); }
            if (fg is not null && size is not null && !fgSizes.Contains((fg.Id, size.Id))) { problems.Add("Size bukan Size Berlaku untuk FG ini."); }

            if (!int.TryParse(Cell("qty"), NumberStyles.None, CultureInfo.InvariantCulture, out var qty) || qty is < 1 or > 1_000_000)
            {
                problems.Add("Qty harus bilangan bulat 1 sampai 1.000.000.");
            }

            var date = today;
            var dateText = Cell("date");
            if (dateText.Length > 0)
            {
                if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    problems.Add("Tanggal harus berformat yyyy-MM-dd.");
                }
                else if (date > today)
                {
                    problems.Add("Tanggal tidak boleh di masa depan.");
                }
            }

            if (problems.Count > 0)
            {
                results.Add(new ImportRowResult(row.LineNumber, key, false, string.Join(" ", problems)));
                continue;
            }

            var stockKey = new StockKey(fg!.Id, color!.Id, size!.Id);
            valid.Add((stockKey, qty, date));
            results.Add(new ImportRowResult(row.LineNumber, key, true,
                openings.Contains(stockKey) ? "Peringatan: sudah ada saldo awal untuk kombinasi ini; impor akan menambah." : null));
        }

        var anyError = results.Any(r => !r.IsValid);
        var committed = false;
        if (!dryRun && !anyError && valid.Count > 0)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await RowLocks.EnsureAndLockStockAsync(db, valid.Select(v => (v.Key.FgId, v.Key.ColorId, v.Key.SizeId)), ct);
            var fgIds = valid.Select(v => v.Key.FgId).Distinct().ToList();
            var balances = (await db.StockBalances.Where(s => fgIds.Contains(s.FgId)).ToListAsync(ct))
                .ToDictionary(s => new StockKey(s.FgId, s.ColorId, s.SizeId));
            var now = time.GetUtcNow();
            foreach (var (key, qty, date) in valid)
            {
                balances[key].OnHand += qty;
                db.StockLedger.Add(new StockLedgerEntry
                {
                    FgId = key.FgId, ColorId = key.ColorId, SizeId = key.SizeId, Kind = StockLedgerKind.OpeningBalance, Qty = qty,
                    SourceType = "OpeningBalance", EntryDate = date, Reason = "Saldo awal (import)", CreatedAt = now, CreatedBy = user.Id,
                });
            }

            audit.Add(AuditEvents.StockOpeningImported, "StockBalance", null, null, new { rows = valid.Count, totalQty = valid.Sum(v => v.Qty) });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            committed = true;
        }

        return new ImportResultDto(results.Count, results.Count(r => r.IsValid), results.Count(r => !r.IsValid), committed, results);
    }
}
