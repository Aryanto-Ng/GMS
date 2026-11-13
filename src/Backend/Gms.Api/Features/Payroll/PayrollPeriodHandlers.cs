using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Payroll;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Gms.Api.Features.Notifications;

namespace Gms.Api.Features.Payroll;

internal static class PeriodRules
{
    public static string CodeOf(DateOnly start, DateOnly end) => $"GJ-{start:yyyyMMdd}-{end:yyyyMMdd}";

    public static void ValidateNotes(string? notes, ErrorBag errors)
    {
        if (notes is { Length: > 500 })
        {
            errors.Add("notes", "Catatan maksimal 500 karakter.");
        }
    }

    /// <summary>Tanggal terawal output aktif (jenis Output/Penyesuaian) atau null bila belum ada.</summary>
    public static Task<DateOnly?> OldestOutputAsync(GmsDbContext db, CancellationToken ct) =>
        db.OutputEntries.AsNoTracking().Where(e => e.Status == DocStatus.Active).MinAsync(e => (DateOnly?)e.WorkDate, ct);
}

/// <summary>Daftar/detail periode dan petunjuk periode berikutnya (AC-PY-01..05, AC-PY-23).</summary>
public sealed class PayrollPeriodQueries(GmsDbContext db, PayrollCalculator calculator, ICurrentUser user)
{
    public async Task<IReadOnlyList<PayrollPeriodListItem>> ListAsync(CancellationToken ct)
    {
        var periods = await db.PayrollPeriods.AsNoTracking().OrderByDescending(p => p.StartDate).ToListAsync(ct);
        var closedIds = periods.Where(p => p.Status == PayrollStatus.Closed).Select(p => p.Id).ToList();
        var counts = await db.PayrollSnapshotWorkers.AsNoTracking().Where(w => closedIds.Contains(w.PeriodId))
            .GroupBy(w => w.PeriodId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var names = await UserNamesAsync(periods, ct);

        var items = new List<PayrollPeriodListItem>();
        foreach (var p in periods)
        {
            var (count, total) = await StatsAsync(p, counts, ct);
            items.Add(new PayrollPeriodListItem(p.Id, p.Code, p.StartDate, p.EndDate, p.Status, count, total, p.Status == PayrollStatus.Open,
                p.ClosedAt, p.ClosedBy is { } by ? names.GetValueOrDefault(by) : null));
        }

        return items;
    }

    public async Task<PayrollPeriodDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.PayrollPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw ProblemException.NotFound("Periode gaji");
        var counts = p.Status == PayrollStatus.Closed
            ? new Dictionary<Guid, int> { [p.Id] = await db.PayrollSnapshotWorkers.CountAsync(w => w.PeriodId == p.Id, ct) }
            : [];
        var (count, total) = await StatsAsync(p, counts, ct);
        var names = await UserNamesAsync([p], ct);
        var open = p.Status == PayrollStatus.Open;
        return new PayrollPeriodDto(p.Id, p.Code, p.StartDate, p.EndDate, p.Status, p.Notes, count, total, open, p.ClosedAt,
            p.ClosedBy is { } by ? names.GetValueOrDefault(by) : null,
            open && user.HasPermission(Permissions.Payroll.ClosePeriod), open && user.HasPermission(Permissions.Payroll.CreatePeriod), p.RowVersion);
    }

    public async Task<NextPeriodDefaultsDto> NextDefaultsAsync(CancellationToken ct)
    {
        var last = await db.PayrollPeriods.AsNoTracking().OrderByDescending(p => p.EndDate).FirstOrDefaultAsync(ct);
        var hasOpen = await db.PayrollPeriods.AnyAsync(p => p.Status == PayrollStatus.Open, ct);
        var oldest = await PeriodRules.OldestOutputAsync(db, ct);
        return last is null
            ? new NextPeriodDefaultsDto(oldest, false, oldest, hasOpen)
            : new NextPeriodDefaultsDto(last.EndDate.AddDays(1), true, oldest, hasOpen);
    }

    private async Task<(int Count, decimal Total)> StatsAsync(PayrollPeriod p, Dictionary<Guid, int> closedCounts, CancellationToken ct)
    {
        if (p.Status == PayrollStatus.Closed)
        {
            return (closedCounts.GetValueOrDefault(p.Id), p.TotalPaid ?? 0m);
        }

        var totals = PayrollCalculator.Totals(await calculator.LoadLiveAsync(p.StartDate, p.EndDate, ct));
        return (totals.Count, totals.Sum(t => t.Paid));
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<PayrollPeriod> periods, CancellationToken ct)
    {
        var ids = periods.Select(p => p.ClosedBy).Where(x => x != null).Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }
}

/// <summary>Membuat periode: berurutan tanpa celah, satu Open, periode pertama tidak lebih akhir dari output tertua (AC-PY-01..04).</summary>
public sealed class CreatePayrollPeriodHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, PayrollPeriodQueries queries)
{
    public async Task<PayrollPeriodDto> HandleAsync(SavePeriodRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        PeriodRules.ValidateNotes(request.Notes, errors);
        errors.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RowLocks.LockPeriodCreationAsync(db, ct);

        if (await db.PayrollPeriods.AnyAsync(p => p.Status == PayrollStatus.Open, ct))
        {
            throw ProblemException.Rule(ProblemCodes.PayrollOpenPeriodExists,
                "Masih ada periode yang Open. Tutup periode tersebut sebelum membuat periode baru.");
        }

        var last = await db.PayrollPeriods.AsNoTracking().OrderByDescending(p => p.EndDate).FirstOrDefaultAsync(ct);
        DateOnly start;
        if (last is not null)
        {
            start = last.EndDate.AddDays(1);
            if (request.StartDate is { } given && given != start)
            {
                throw ProblemException.Rule(ProblemCodes.PayrollNotContiguous,
                    $"Tanggal mulai periode baru harus {start:dd-MM-yyyy} (sehari setelah periode sebelumnya) dan tidak dapat diubah.");
            }
        }
        else
        {
            start = request.StartDate ?? throw ProblemException.Validation("startDate", "Tanggal mulai periode pertama wajib diisi.");
            await EnsureNotAfterOldestAsync(start, ct);
        }

        if (request.EndDate < start)
        {
            throw ProblemException.Validation("endDate", "Tanggal selesai tidak boleh sebelum tanggal mulai.");
        }

        var period = new PayrollPeriod
        {
            Code = PeriodRules.CodeOf(start, request.EndDate), StartDate = start, EndDate = request.EndDate,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(), CreatedAt = time.GetUtcNow(), CreatedBy = user.Id,
        };
        db.PayrollPeriods.Add(period);
        audit.Add(AuditEvents.PayrollPeriodCreated, "PayrollPeriod", period.Id.ToString(), null, new { period.Code, period.StartDate, period.EndDate });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw ProblemException.Rule(ProblemCodes.PayrollOpenPeriodExists, "Periode dengan rentang atau status yang sama sudah ada.");
        }

        await tx.CommitAsync(ct);
        return await queries.GetAsync(period.Id, ct);
    }

    private async Task EnsureNotAfterOldestAsync(DateOnly start, CancellationToken ct)
    {
        await RowLocks.LockOutputsAgainstInsertAsync(db, ct);
        var oldest = await PeriodRules.OldestOutputAsync(db, ct);
        if (oldest is { } date && start > date)
        {
            throw ProblemException.Rule(ProblemCodes.PayrollStartAfterOldestOutput,
                $"Tanggal mulai periode pertama tidak boleh lebih akhir dari output tertua ({date:dd-MM-yyyy}).");
        }
    }
}

/// <summary>Ubah periode Open: tanggal selesai dan catatan; tanggal mulai hanya untuk periode pertama (AC-PY-05).</summary>
public sealed class UpdatePayrollPeriodHandler(
    GmsDbContext db, ICurrentUser user, IAuditWriter audit, TimeProvider time, PayrollPeriodQueries queries)
{
    public async Task<PayrollPeriodDto> HandleAsync(Guid id, SavePeriodRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        PeriodRules.ValidateNotes(request.Notes, errors);
        errors.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.PayrollPeriods.AnyAsync(p => p.Id == id, ct))
        {
            throw ProblemException.NotFound("Periode gaji");
        }

        await RowLocks.LockPayrollPeriodAsync(db, id, ct);
        var period = await db.PayrollPeriods.SingleAsync(p => p.Id == id, ct);
        db.Entry(period).Property(p => p.RowVersion).OriginalValue = request.RowVersion;
        if (period.Status != PayrollStatus.Open)
        {
            throw ProblemException.StateConflict("Periode yang sudah ditutup tidak dapat diubah.");
        }

        var before = new { period.StartDate, period.EndDate, period.Notes };
        var start = period.StartDate;
        if (request.StartDate is { } given && given != period.StartDate)
        {
            var isFirst = !await db.PayrollPeriods.AnyAsync(p => p.EndDate < period.StartDate, ct);
            if (!isFirst)
            {
                throw ProblemException.Rule(ProblemCodes.PayrollNotContiguous, "Tanggal mulai hanya dapat diubah pada periode pertama.");
            }

            await RowLocks.LockOutputsAgainstInsertAsync(db, ct);
            var oldest = await PeriodRules.OldestOutputAsync(db, ct);
            if (oldest is { } date && given > date)
            {
                throw ProblemException.Rule(ProblemCodes.PayrollStartAfterOldestOutput,
                    $"Tanggal mulai periode pertama tidak boleh lebih akhir dari output tertua ({date:dd-MM-yyyy}).");
            }

            start = given;
        }

        if (request.EndDate < start)
        {
            throw ProblemException.Validation("endDate", "Tanggal selesai tidak boleh sebelum tanggal mulai.");
        }

        period.StartDate = start;
        period.EndDate = request.EndDate;
        period.Code = PeriodRules.CodeOf(start, request.EndDate);
        period.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        period.UpdatedAt = time.GetUtcNow();
        period.UpdatedBy = user.Id;
        audit.Add(AuditEvents.PayrollPeriodUpdated, "PayrollPeriod", id.ToString(), before, new { period.StartDate, period.EndDate, period.Notes });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await queries.GetAsync(id, ct);
    }
}

/// <summary>Pratinjau penutupan: ringkasan dan peringatan (AC-PY-15/16). Tarif hilang ditolak lebih awal agar pengguna tahu sebelum menutup.</summary>
public sealed class PreviewClosePayrollPeriodHandler(GmsDbContext db, PayrollCalculator calculator, AppTimeZone zone, TimeProvider time)
{
    public async Task<ClosePreviewDto> HandleAsync(Guid id, CancellationToken ct)
    {
        var period = await db.PayrollPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ProblemException.NotFound("Periode gaji");
        if (period.Status != PayrollStatus.Open)
        {
            throw ProblemException.StateConflict("Periode sudah ditutup.");
        }

        var lines = await calculator.LoadLiveAsync(period.StartDate, period.EndDate, ct);
        ClosePayrollPeriodHandler.EnsureTariffsPresent(lines);
        var totals = PayrollCalculator.Totals(lines);
        return new ClosePreviewDto(
            totals.Count, totals.Sum(t => t.Paid),
            totals.Where(t => t.IsNegative).Select(t => new PayrollNegativeWorker(t.WorkerId, t.WorkerCode, t.WorkerName, t.Paid)).ToList(),
            period.EndDate > zone.Today(time), period.Code);
    }
}

/// <summary>
/// Penutupan periode: kunci eksklusif periode (menunggu input output yang berjalan, menolak yang datang kemudian), snapshot beku
/// baris + total pekerja, status Closed, semuanya atomik dan permanen (AC-PY-15..22).
/// </summary>
public sealed class ClosePayrollPeriodHandler(
    GmsDbContext db, PayrollCalculator calculator, ICurrentUser user, IAuditWriter audit, TimeProvider time, PayrollPeriodQueries queries,
    NotificationEmitter notify)
{
    public static void EnsureTariffsPresent(List<PayrollLine> lines)
    {
        var missing = lines.Where(l => l.RateMissing).GroupBy(l => (l.WoCode, l.ProcessName, l.SizeCode)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var violations = missing.Select(g => new RuleViolation(ProblemCodes.PayrollTariffMissing, $"{g.Key.WoCode} · {g.Key.ProcessName} · {g.Key.SizeCode}",
            g.Sum(l => l.Qty), null, $"Tarif snapshot WO tidak ditemukan untuk {g.Key.WoCode} / {g.Key.ProcessName} / {g.Key.SizeCode} ({g.Count()} baris).")).ToList();
        throw ProblemException.Rule(ProblemCodes.PayrollTariffMissing,
            "Penutupan ditolak: ada baris tanpa tarif snapshot (data tidak konsisten). Laporkan kepada administrator.", violations);
    }

    public async Task<PayrollPeriodDto> HandleAsync(Guid id, ClosePeriodRequest request, CancellationToken ct)
    {
        if (!await db.PayrollPeriods.AnyAsync(p => p.Id == id, ct))
        {
            throw ProblemException.NotFound("Periode gaji");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RowLocks.LockPayrollPeriodAsync(db, id, ct);
        var period = await db.PayrollPeriods.SingleAsync(p => p.Id == id, ct);
        db.Entry(period).Property(p => p.RowVersion).OriginalValue = request.RowVersion;
        if (period.Status != PayrollStatus.Open)
        {
            throw ProblemException.StateConflict("Periode sudah ditutup.");
        }

        if (!string.Equals(request.ConfirmationCode?.Trim(), period.Code, StringComparison.Ordinal))
        {
            throw ProblemException.Validation("confirmationCode", $"Ketik kode periode {period.Code} untuk mengonfirmasi penutupan.");
        }

        var lines = await calculator.LoadLiveAsync(period.StartDate, period.EndDate, ct);
        EnsureTariffsPresent(lines);
        var totals = PayrollCalculator.Totals(lines);

        foreach (var l in lines)
        {
            db.PayrollSnapshotLines.Add(new PayrollSnapshotLine
            {
                PeriodId = id, WorkerId = l.WorkerId, WorkerCode = l.WorkerCode, WorkerName = l.WorkerName, WoId = l.WoId, WoCode = l.WoCode,
                ProcessCode = l.ProcessCode, ProcessName = l.ProcessName, SizeCode = l.SizeCode, WorkDate = l.WorkDate, Kind = l.Kind,
                OriginalDate = l.OriginalDate, Reason = l.Reason, Qty = l.Qty, Rate = l.Rate, Subtotal = l.Subtotal, SourceEntryId = l.SourceEntryId,
            });
        }

        foreach (var t in totals)
        {
            db.PayrollSnapshotWorkers.Add(new PayrollSnapshotWorker
            {
                PeriodId = id, WorkerId = t.WorkerId, WorkerCode = t.WorkerCode, WorkerName = t.WorkerName, TotalQty = t.TotalQty,
                SumBeforeRounding = t.SumBeforeRounding, PaidAmount = t.Paid,
            });
        }

        var now = time.GetUtcNow();
        period.Status = PayrollStatus.Closed;
        period.ClosedAt = now;
        period.ClosedBy = user.Id;
        period.TotalPaid = totals.Sum(t => t.Paid);
        period.UpdatedAt = now;
        period.UpdatedBy = user.Id;
        await notify.PayrollClosedAsync(period, ct);
        audit.Add(AuditEvents.PayrollPeriodClosed, "PayrollPeriod", id.ToString(), new { status = "Open" },
            new { status = "Closed", period.Code, workers = totals.Count, lines = lines.Count, total = period.TotalPaid });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await queries.GetAsync(id, ct);
    }
}
