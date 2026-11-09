using Gms.Api.Common;
using Gms.Domain.Payroll;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Payroll;

/// <summary>
/// Pembaca rekap payroll (AC-PY-07..14, AC-PY-24). Periode Open dihitung langsung (provisional), Closed dari snapshot.
/// Seluruh endpoint memakai <c>Payroll.View</c> karena memuat tarif dan upah.
/// </summary>
public sealed class PayrollQueries(GmsDbContext db, PayrollCalculator calculator)
{
    public async Task<(PayrollPeriod Period, List<PayrollLine> Lines)> LoadAsync(Guid periodId, CancellationToken ct)
    {
        var period = await db.PayrollPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.Id == periodId, ct)
                     ?? throw ProblemException.NotFound("Periode gaji");
        return (period, await calculator.LoadAsync(period, ct));
    }

    public async Task<PayrollSummaryDto> GetSummaryAsync(Guid periodId, string? q, CancellationToken ct)
    {
        var (period, lines) = await LoadAsync(periodId, ct);
        return PayrollCalculator.Summary(lines, period.Status == PayrollStatus.Open, q);
    }

    public async Task<PayrollWorkerDetailDto> GetWorkerDetailAsync(Guid periodId, Guid workerId, CancellationToken ct)
    {
        var (_, lines) = await LoadAsync(periodId, ct);
        return PayrollCalculator.WorkerDetail(lines, workerId) ?? throw ProblemException.NotFound("Pekerja pada periode ini");
    }

    public async Task<PayrollDailyResult> GetDailyAsync(Guid periodId, PayrollDailyQuery query, CancellationToken ct)
    {
        var (_, lines) = await LoadAsync(periodId, ct);
        IEnumerable<PayrollLine> filtered = lines;
        if (query.From is { } from) { filtered = filtered.Where(l => l.WorkDate >= from); }
        if (query.To is { } to) { filtered = filtered.Where(l => l.WorkDate <= to); }
        if (query.WorkerId is { } workerId) { filtered = filtered.Where(l => l.WorkerId == workerId); }
        if (query.WoId is { } woId) { filtered = filtered.Where(l => l.WoId == woId); }
        if (!string.IsNullOrWhiteSpace(query.ProcessCode))
        {
            filtered = filtered.Where(l => string.Equals(l.ProcessCode, query.ProcessCode.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var text = query.Q.Trim();
            filtered = filtered.Where(l => l.WorkerCode.Contains(text, StringComparison.OrdinalIgnoreCase)
                                           || l.WorkerName.Contains(text, StringComparison.OrdinalIgnoreCase)
                                           || l.WoCode.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        var all = filtered.ToList(); // urutan sudah: tanggal kerja, pekerja, WO, proses, size
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var rows = all.Skip((page - 1) * pageSize).Take(pageSize).Select(PayrollCalculator.ToDailyRow).ToList();
        return new PayrollDailyResult(new PagedResult<PayrollDailyRow>(rows, page, pageSize, all.Count), all.Sum(l => l.Subtotal));
    }

    public async Task<PayrollMatrixDto> GetMatrixAsync(Guid periodId, CancellationToken ct)
    {
        var (period, lines) = await LoadAsync(periodId, ct);
        var dates = Enumerable.Range(0, period.EndDate.DayNumber - period.StartDate.DayNumber + 1).Select(period.StartDate.AddDays).ToList();
        var rows = lines.GroupBy(l => (l.WorkerId, l.WorkerCode, l.WorkerName)).OrderBy(g => g.Key.WorkerCode, StringComparer.Ordinal)
            .Select(g => new PayrollMatrixRow(
                g.Key.WorkerId, g.Key.WorkerCode, g.Key.WorkerName,
                g.GroupBy(l => l.WorkDate).OrderBy(d => d.Key).Select(d => new PayrollMatrixCell(d.Key, d.Sum(x => x.Qty), d.Sum(x => x.Subtotal))).ToList(),
                g.Sum(l => l.Qty), g.Sum(l => l.Subtotal))).ToList();
        var dayTotals = lines.GroupBy(l => l.WorkDate).OrderBy(g => g.Key)
            .Select(g => new PayrollMatrixCell(g.Key, g.Sum(x => x.Qty), g.Sum(x => x.Subtotal))).ToList();
        return new PayrollMatrixDto(dates, rows, dayTotals);
    }

    public async Task<IReadOnlyList<PayrollDailyRow>> GetAdjustmentsAsync(Guid periodId, CancellationToken ct)
    {
        var (_, lines) = await LoadAsync(periodId, ct);
        return lines.Where(l => l.Kind == OutputKind.Adjustment).Select(PayrollCalculator.ToDailyRow).ToList();
    }
}
