using Gms.Domain.Payroll;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Payroll;

/// <summary>Satu baris upah: qty (bertanda) × tarif snapshot WO untuk satu entri output dan satu Size (Color dijumlahkan, AC-PY-10).</summary>
public sealed record PayrollLine(
    Guid SourceEntryId, DateOnly WorkDate, Guid WorkerId, string WorkerCode, string WorkerName, Guid WoId, string WoCode,
    string ProcessCode, string ProcessName, string SizeCode, int SizeSort, OutputKind Kind, DateOnly? OriginalDate, string? Reason,
    int Qty, decimal Rate, bool RateMissing)
{
    /// <summary>Tidak dibulatkan (presisi tarif 5 desimal).</summary>
    public decimal Subtotal => Qty * Rate;
}

/// <summary>Total per pekerja: dibulatkan SEKALI per pekerja (setengah ke atas menjauhi nol, FSD Payroll 2.3).</summary>
public sealed record WorkerTotal(Guid WorkerId, string WorkerCode, string WorkerName, int TotalQty, decimal SumBeforeRounding, decimal Paid)
{
    public bool IsNegative => Paid < 0 || SumBeforeRounding < 0;
}

/// <summary>
/// Sumber perhitungan payroll. Periode Open dihitung LANGSUNG dari output aktif + tarif snapshot WO; periode Closed selalu dibaca dari
/// snapshot beku (AC-PY-11/18). Tipe desimal dipakai di seluruh perhitungan, tanpa floating point.
/// </summary>
public sealed class PayrollCalculator(GmsDbContext db)
{
    public static decimal Round(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    public static List<WorkerTotal> Totals(IEnumerable<PayrollLine> lines) =>
        lines.GroupBy(l => (l.WorkerId, l.WorkerCode, l.WorkerName))
            .Select(g =>
            {
                var sum = g.Sum(l => l.Subtotal);
                return new WorkerTotal(g.Key.WorkerId, g.Key.WorkerCode, g.Key.WorkerName, g.Sum(l => l.Qty), sum, Round(sum));
            })
            .OrderBy(t => t.WorkerCode, StringComparer.Ordinal).ToList();

    public Task<List<PayrollLine>> LoadAsync(PayrollPeriod period, CancellationToken ct) =>
        period.Status == PayrollStatus.Closed ? LoadSnapshotAsync(period.Id, ct) : LoadLiveAsync(period.StartDate, period.EndDate, ct);

    public async Task<List<PayrollLine>> LoadSnapshotAsync(Guid periodId, CancellationToken ct)
    {
        var rows = await db.PayrollSnapshotLines.AsNoTracking().Where(l => l.PeriodId == periodId).ToListAsync(ct);
        var sizeSort = await db.Sizes.AsNoTracking().Select(s => new { s.Code, s.SortOrder }).ToListAsync(ct);
        var sort = sizeSort.GroupBy(s => s.Code).ToDictionary(g => g.Key, g => g.First().SortOrder);
        return rows.Select(l => new PayrollLine(
            l.SourceEntryId, l.WorkDate, l.WorkerId, l.WorkerCode, l.WorkerName, l.WoId, l.WoCode, l.ProcessCode, l.ProcessName, l.SizeCode,
            sort.GetValueOrDefault(l.SizeCode), l.Kind, l.OriginalDate, l.Reason, l.Qty, l.Rate, false))
            .OrderBy(l => l.WorkDate).ThenBy(l => l.WorkerCode, StringComparer.Ordinal).ThenBy(l => l.WoCode, StringComparer.Ordinal)
            .ThenBy(l => l.ProcessCode, StringComparer.Ordinal).ThenBy(l => l.SizeSort).ToList();
    }

    private sealed class Raw
    {
        public Guid EntryId { get; set; }
        public Guid WorkerId { get; set; }
        public string WorkerCode { get; set; } = string.Empty;
        public string WorkerName { get; set; } = string.Empty;
        public Guid WoId { get; set; }
        public string WoCode { get; set; } = string.Empty;
        public int Seq { get; set; }
        public string ProcessCode { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public Guid SizeId { get; set; }
        public string SizeCode { get; set; } = string.Empty;
        public int SizeSort { get; set; }
        public DateOnly WorkDate { get; set; }
        public OutputKind Kind { get; set; }
        public DateOnly? OriginalDate { get; set; }
        public string? Reason { get; set; }
        public int Qty { get; set; }
    }

    public async Task<List<PayrollLine>> LoadLiveAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var raw = await (
            from e in db.OutputEntries.AsNoTracking()
            where e.Status == DocStatus.Active && e.WorkDate >= start && e.WorkDate <= end
            join l in db.OutputEntryLines.AsNoTracking() on e.Id equals l.EntryId
            join wl in db.WoLines.AsNoTracking() on l.WoLineId equals wl.Id
            join pl in db.PoLines.AsNoTracking() on wl.PoLineId equals pl.Id
            join sz in db.Sizes.AsNoTracking() on pl.SizeId equals sz.Id
            join w in db.WorkOrders.AsNoTracking() on e.WoId equals w.Id
            join r in db.WoRoutingSnapshots.AsNoTracking() on new { e.WoId, e.Seq } equals new { r.WoId, r.Seq }
            join wk in db.Workers.AsNoTracking() on e.WorkerId equals wk.Id
            select new Raw
            {
                EntryId = e.Id, WorkerId = wk.Id, WorkerCode = wk.Code, WorkerName = wk.Name, WoId = w.Id, WoCode = w.Code, Seq = e.Seq,
                ProcessCode = r.ProcessCode, ProcessName = r.ProcessName, SizeId = sz.Id, SizeCode = sz.Code, SizeSort = sz.SortOrder,
                WorkDate = e.WorkDate, Kind = e.Kind, OriginalDate = e.OriginalDate, Reason = e.Reason, Qty = l.Qty,
            }).ToListAsync(ct);

        var woIds = raw.Select(r => r.WoId).Distinct().ToList();
        var rates = await db.WoTariffSnapshots.AsNoTracking().Where(t => woIds.Contains(t.WoId)).ToListAsync(ct);
        var rateMap = rates.ToDictionary(t => (t.WoId, t.Seq, t.SizeId), t => t.Rate);

        return raw
            .GroupBy(r => (r.EntryId, r.SizeId))
            .Select(g =>
            {
                var first = g.First();
                var found = rateMap.TryGetValue((first.WoId, first.Seq, first.SizeId), out var rate);
                return new PayrollLine(
                    first.EntryId, first.WorkDate, first.WorkerId, first.WorkerCode, first.WorkerName, first.WoId, first.WoCode,
                    first.ProcessCode, first.ProcessName, first.SizeCode, first.SizeSort, first.Kind, first.OriginalDate, first.Reason,
                    g.Sum(x => x.Qty), found ? rate : 0m, !found);
            })
            .Where(l => l.Qty != 0)
            .OrderBy(l => l.WorkDate).ThenBy(l => l.WorkerCode, StringComparer.Ordinal).ThenBy(l => l.WoCode, StringComparer.Ordinal)
            .ThenBy(l => l.ProcessCode, StringComparer.Ordinal).ThenBy(l => l.SizeSort).ToList();
    }

    // ===== Bentuk tampilan =====

    public static PayrollDailyRow ToDailyRow(PayrollLine l) =>
        new(l.WorkDate, l.WorkerId, l.WorkerCode, l.WorkerName, l.WoId, l.WoCode, l.ProcessName, l.SizeCode, l.Kind, l.Qty, l.Rate, l.Subtotal,
            l.OriginalDate, l.Reason, l.SourceEntryId);

    public static PayrollSummaryDto Summary(List<PayrollLine> lines, bool provisional, string? q)
    {
        var totals = Totals(lines);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            totals = totals.Where(t => t.WorkerCode.Contains(text, StringComparison.OrdinalIgnoreCase)
                                       || t.WorkerName.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return new PayrollSummaryDto(
            totals.Select(t => new PayrollSummaryRow(t.WorkerId, t.WorkerCode, t.WorkerName, t.TotalQty, t.SumBeforeRounding, t.Paid, t.IsNegative)).ToList(),
            totals.Sum(t => t.Paid), totals.Count, provisional);
    }

    public static PayrollWorkerDetailDto WorkerDetail(List<PayrollLine> lines, Guid workerId)
    {
        var mine = lines.Where(l => l.WorkerId == workerId).ToList();
        if (mine.Count == 0)
        {
            return null!;
        }

        var total = Totals(mine).Single();
        var byWo = mine.GroupBy(l => (l.WoId, l.WoCode, l.ProcessCode, l.ProcessName))
            .OrderBy(g => g.Key.WoCode, StringComparer.Ordinal).ThenBy(g => g.Key.ProcessCode, StringComparer.Ordinal)
            .Select(g => new PayrollWoGroup(
                g.Key.WoId, g.Key.WoCode, g.Key.ProcessName,
                g.GroupBy(l => (l.SizeCode, l.SizeSort, l.Rate)).OrderBy(s => s.Key.SizeSort)
                    .Select(s => new PayrollSizeLine(s.Key.SizeCode, s.Sum(x => x.Qty), s.Key.Rate, s.Sum(x => x.Subtotal))).ToList(),
                g.Sum(l => l.Subtotal))).ToList();
        var byDay = mine.GroupBy(l => l.WorkDate).OrderBy(g => g.Key)
            .Select(g => new PayrollDayGroup(g.Key, g.Select(ToDailyRow).ToList(), g.Sum(l => l.Subtotal))).ToList();
        var adjustments = mine.Where(l => l.Kind == OutputKind.Adjustment).Select(ToDailyRow).ToList();
        return new PayrollWorkerDetailDto(workerId, total.WorkerCode, total.WorkerName, total.TotalQty, total.SumBeforeRounding, total.Paid, byWo, byDay, adjustments);
    }
}
