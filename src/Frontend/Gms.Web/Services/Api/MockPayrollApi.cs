using System.Text;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Payroll;
using Gms.Shared.Problems;

namespace Gms.Web.Services.Api;

/// <summary>
/// Payroll Borongan tiruan (ADR-014): periode berurutan (satu Open), upah = Qty × tarif snapshot WO per entri × Size, total pekerja dibulatkan SEKALI (setengah menjauhi nol),
/// Open = sementara (hitung langsung), Closed = snapshot beku. Penutupan mengunci tanggal pada <see cref="MockProductionApi.ClosedUntil"/>.
/// </summary>
public sealed class MockPayrollApi : IPayrollApi
{
    private sealed class Period
    {
        public Guid Id = Guid.NewGuid(); public DateOnly Start, End; public PayrollStatus Status; public string? Notes; public DateTimeOffset? ClosedAt; public uint Version = 1;
        public List<PayrollDailyRow>? Snapshot;
    }

    private readonly object _gate = new();
    private readonly List<Period> _periods = [];
    private readonly MockProductionApi _production;
    private readonly MockWoApi _wos;
    private readonly MockWorkerApi _workers;
    private readonly MockProcessApi _processes;
    private readonly MockSizeApi _sizes;

    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public MockPayrollApi(MockProductionApi production, MockWoApi wos, MockWorkerApi workers, MockProcessApi processes, MockSizeApi sizes)
    {
        _production = production;
        _wos = wos;
        _workers = workers;
        _processes = processes;
        _sizes = sizes;
        production.ClosedUntil = () => _periods.Where(p => p.Status == PayrollStatus.Closed).Select(p => (DateOnly?)p.End).DefaultIfEmpty().Max();
    }

    private static ApiException Problem(string type, int status, string title, string? detail = null, string? code = null, IReadOnlyList<RuleViolation>? violations = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, Code = code, Violations = violations, Errors = errors, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    private static ApiException Invalid(string path, string message) =>
        Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: new Dictionary<string, string[]> { [path] = [message] });

    private static string CodeOf(DateOnly start, DateOnly end) => $"GJ-{start:yyyyMMdd}-{end:yyyyMMdd}";

    private Period Find(Guid id) => _periods.FirstOrDefault(p => p.Id == id) ?? throw Problem(ProblemTypes.NotFound, 404, "Periode tidak ditemukan.");

    private static decimal Round(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);

    // ----- Perhitungan -----

    /// <summary>Baris upah live untuk rentang tanggal; <c>Rate</c> null (tarif hilang) dikembalikan terpisah agar penutupan dapat menolak.</summary>
    private (List<PayrollDailyRow> Rows, List<RuleViolation> Missing) Compute(DateOnly start, DateOnly end)
    {
        var rows = new List<PayrollDailyRow>();
        var missing = new List<RuleViolation>();
        foreach (var s in _production.WageSources().Where(s => s.Date >= start && s.Date <= end))
        {
            var wo = _wos.GetAsync(s.WoId).GetAwaiter().GetResult();
            var line = wo.Lines.First(l => l.PoLineId == s.WoLineId);
            var step = _wos.GetProgressAsync(s.WoId).GetAwaiter().GetResult().Steps.First(x => x.Seq == s.Seq);
            var rate = _wos.RateFor(s.WoId, s.Seq, line.ColorSize.SizeId);
            var worker = _workers.GetAsync(s.WorkerId).GetAwaiter().GetResult();
            if (rate is null)
            {
                missing.Add(new RuleViolation(ProblemCodes.PayrollTariffMissing, $"{wo.Code} · {step.ProcessName} · {line.ColorSize.SizeCode}", null, null, "Tarif snapshot belum ada."));
                continue;
            }

            rows.Add(new PayrollDailyRow(s.Date, worker.Id, worker.Code, worker.Name, wo.Id, wo.Code, step.ProcessName, line.ColorSize.SizeCode, s.Kind, s.Qty, rate.Value, s.Qty * rate.Value, s.OriginalDate, s.Reason, s.EntryId));
        }

        return (rows, missing);
    }

    private List<PayrollDailyRow> RowsOf(Period p) => p.Status == PayrollStatus.Closed ? p.Snapshot! : Compute(p.Start, p.End).Rows;

    private static List<PayrollSummaryRow> Summarize(IEnumerable<PayrollDailyRow> rows) =>
        rows.GroupBy(r => (r.WorkerId, r.WorkerCode, r.WorkerName)).OrderBy(g => g.Key.WorkerCode)
            .Select(g =>
            {
                var sum = g.Sum(r => r.Subtotal);
                var paid = Round(sum);
                return new PayrollSummaryRow(g.Key.WorkerId, g.Key.WorkerCode, g.Key.WorkerName, g.Sum(r => r.Qty), sum, paid, paid < 0);
            }).ToList();

    private PayrollPeriodDto Dto(Period p)
    {
        var summary = Summarize(RowsOf(p));
        var open = p.Status == PayrollStatus.Open;
        return new PayrollPeriodDto(p.Id, CodeOf(p.Start, p.End), p.Start, p.End, p.Status, p.Notes, summary.Count, summary.Sum(s => s.Paid), open, p.ClosedAt, p.ClosedAt is null ? null : "Administrator", open, open, p.Version);
    }

    // ----- Periode -----

    public Task<IReadOnlyList<PayrollPeriodListItem>> ListPeriodsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<PayrollPeriodListItem> list = _periods.OrderByDescending(p => p.Start).Select(p =>
            {
                var d = Dto(p);
                return new PayrollPeriodListItem(p.Id, d.Code, p.Start, p.End, p.Status, d.WorkerCount, d.TotalPaid, d.IsProvisional, p.ClosedAt, d.ClosedByName);
            }).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<NextPeriodDefaultsDto> GetNextPeriodDefaultsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var last = _periods.OrderByDescending(p => p.End).FirstOrDefault();
            var oldest = _production.WageSources().Select(s => (DateOnly?)s.Date).DefaultIfEmpty().Min();
            return Task.FromResult(new NextPeriodDefaultsDto(last is null ? oldest : last.End.AddDays(1), last is not null, oldest, _periods.Any(p => p.Status == PayrollStatus.Open)));
        }
    }

    public Task<PayrollPeriodDto> CreatePeriodAsync(SavePeriodRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_periods.Any(p => p.Status == PayrollStatus.Open)) { throw Problem(ProblemTypes.RuleViolation, 409, "Sudah ada periode Open.", "Tutup periode yang terbuka lebih dulu.", ProblemCodes.PayrollOpenPeriodExists); }
            var last = _periods.OrderByDescending(p => p.End).FirstOrDefault();
            var start = last is null ? r.StartDate ?? throw Invalid("startDate", "Tanggal mulai wajib untuk periode pertama.") : last.End.AddDays(1);
            if (last is not null && r.StartDate is { } given && given != start)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Tanggal mulai harus sehari setelah periode sebelumnya.", code: ProblemCodes.PayrollNotContiguous);
            }

            if (last is null && _production.WageSources().Select(s => (DateOnly?)s.Date).DefaultIfEmpty().Min() is { } oldest && start > oldest)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Tanggal mulai setelah output tertua.", $"Output tertua {oldest:yyyy-MM-dd}.", ProblemCodes.PayrollStartAfterOldestOutput);
            }

            if (r.EndDate < start) { throw Invalid("endDate", "Tanggal selesai tidak boleh sebelum tanggal mulai."); }
            var p = new Period { Start = start, End = r.EndDate, Status = PayrollStatus.Open, Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim() };
            _periods.Add(p);
            return Task.FromResult(Dto(p));
        }
    }

    public Task<PayrollPeriodDto> GetPeriodAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(Dto(Find(id))); }
    }

    public Task<PayrollPeriodDto> UpdatePeriodAsync(Guid id, SavePeriodRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = Find(id);
            if (p.Version != r.RowVersion) { throw Problem(ProblemTypes.ConcurrencyConflict, 409, "Periode telah diubah pengguna lain.", "Muat ulang halaman."); }
            if (p.Status != PayrollStatus.Open) { throw Problem(ProblemTypes.StateConflict, 409, "Periode sudah Closed."); }
            if (r.StartDate is { } s && s != p.Start)
            {
                if (_periods.Count > 1) { throw Problem(ProblemTypes.RuleViolation, 409, "Hanya periode pertama yang dapat mengubah tanggal mulai.", code: ProblemCodes.PayrollNotContiguous); }
                p.Start = s;
            }

            if (r.EndDate < p.Start) { throw Invalid("endDate", "Tanggal selesai tidak boleh sebelum tanggal mulai."); }
            p.End = r.EndDate;
            p.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
            p.Version++;
            return Task.FromResult(Dto(p));
        }
    }

    public Task<ClosePreviewDto> PreviewClosePeriodAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = Find(id);
            if (p.Status != PayrollStatus.Open) { throw Problem(ProblemTypes.StateConflict, 409, "Periode sudah Closed."); }
            var (rows, missing) = Compute(p.Start, p.End);
            if (missing.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Tarif snapshot belum lengkap.", code: ProblemCodes.PayrollTariffMissing, violations: missing); }
            var summary = Summarize(rows);
            return Task.FromResult(new ClosePreviewDto(summary.Count, summary.Sum(s => s.Paid), summary.Where(s => s.IsNegative).Select(s => new PayrollNegativeWorker(s.WorkerId, s.WorkerCode, s.WorkerName, s.Paid)).ToList(), p.End > Today(), CodeOf(p.Start, p.End)));
        }
    }

    public Task<PayrollPeriodDto> ClosePeriodAsync(Guid id, ClosePeriodRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = Find(id);
            if (p.Version != r.RowVersion) { throw Problem(ProblemTypes.ConcurrencyConflict, 409, "Periode telah diubah pengguna lain.", "Muat ulang halaman."); }
            if (p.Status != PayrollStatus.Open) { throw Problem(ProblemTypes.StateConflict, 409, "Periode sudah Closed."); }
            if (!string.Equals(r.ConfirmationCode?.Trim(), CodeOf(p.Start, p.End), StringComparison.Ordinal)) { throw Invalid("confirmationCode", "Kode konfirmasi tidak sesuai."); }
            var (rows, missing) = Compute(p.Start, p.End);
            if (missing.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Tarif snapshot belum lengkap.", code: ProblemCodes.PayrollTariffMissing, violations: missing); }
            p.Snapshot = rows;
            p.Status = PayrollStatus.Closed;
            p.ClosedAt = DateTimeOffset.UtcNow;
            p.Version++;
            return Task.FromResult(Dto(p));
        }
    }

    // ----- Rekap & rincian -----

    public Task<PayrollSummaryDto> GetSummaryAsync(Guid periodId, string? q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = Find(periodId);
            var rows = Summarize(RowsOf(p));
            var total = rows.Sum(r => r.Paid);
            IEnumerable<PayrollSummaryRow> shown = rows;
            if (!string.IsNullOrWhiteSpace(q)) { shown = rows.Where(r => r.WorkerName.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase) || r.WorkerCode.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)); }
            return Task.FromResult(new PayrollSummaryDto(shown.ToList(), total, rows.Count, p.Status == PayrollStatus.Open));
        }
    }

    public Task<PayrollWorkerDetailDto> GetWorkerDetailAsync(Guid periodId, Guid workerId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var rows = RowsOf(Find(periodId)).Where(r => r.WorkerId == workerId).ToList();
            if (rows.Count == 0) { throw Problem(ProblemTypes.NotFound, 404, "Pekerja tidak memiliki output pada periode ini."); }
            var sum = rows.Sum(r => r.Subtotal);
            var byWo = rows.Where(r => r.Kind == OutputKind.Output).GroupBy(r => (r.WoId, r.WoCode, r.ProcessName)).Select(g =>
                new PayrollWoGroup(g.Key.WoId, g.Key.WoCode, g.Key.ProcessName, g.GroupBy(r => (r.SizeCode, r.Rate)).Select(s => new PayrollSizeLine(s.Key.SizeCode, s.Sum(r => r.Qty), s.Key.Rate, s.Sum(r => r.Subtotal))).OrderBy(l => l.SizeCode).ToList(), g.Sum(r => r.Subtotal))).ToList();
            var byDay = rows.GroupBy(r => r.WorkDate).OrderBy(g => g.Key).Select(g => new PayrollDayGroup(g.Key, g.ToList(), g.Sum(r => r.Subtotal))).ToList();
            return Task.FromResult(new PayrollWorkerDetailDto(workerId, rows[0].WorkerCode, rows[0].WorkerName, rows.Sum(r => r.Qty), sum, Round(sum), byWo, byDay, rows.Where(r => r.Kind == OutputKind.Adjustment).ToList()));
        }
    }

    public Task<PayrollDailyResult> GetDailyAsync(Guid periodId, PayrollDailyQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<PayrollDailyRow> rows = RowsOf(Find(periodId));
            if (q.From is { } f) { rows = rows.Where(r => r.WorkDate >= f); }
            if (q.To is { } t) { rows = rows.Where(r => r.WorkDate <= t); }
            if (q.WorkerId is { } w) { rows = rows.Where(r => r.WorkerId == w); }
            if (q.WoId is { } wo) { rows = rows.Where(r => r.WoId == wo); }
            if (!string.IsNullOrWhiteSpace(q.ProcessCode)) { rows = rows.Where(r => r.ProcessName.Contains(q.ProcessCode, StringComparison.OrdinalIgnoreCase)); }
            var all = rows.OrderBy(r => r.WorkDate).ThenBy(r => r.WorkerCode).ToList();
            return Task.FromResult(new PayrollDailyResult(new PagedResult<PayrollDailyRow>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), q.Page, q.PageSize, all.Count), all.Sum(r => r.Subtotal)));
        }
    }

    public Task<PayrollMatrixDto> GetMatrixAsync(Guid periodId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = Find(periodId);
            var rows = RowsOf(p);
            var dates = Enumerable.Range(0, p.End.DayNumber - p.Start.DayNumber + 1).Select(i => p.Start.AddDays(i)).ToList();
            var matrix = rows.GroupBy(r => (r.WorkerId, r.WorkerCode, r.WorkerName)).OrderBy(g => g.Key.WorkerCode).Select(g =>
                new PayrollMatrixRow(g.Key.WorkerId, g.Key.WorkerCode, g.Key.WorkerName,
                    g.GroupBy(r => r.WorkDate).OrderBy(d => d.Key).Select(d => new PayrollMatrixCell(d.Key, d.Sum(r => r.Qty), d.Sum(r => r.Subtotal))).ToList(), g.Sum(r => r.Qty), Round(g.Sum(r => r.Subtotal)))).ToList();
            var totals = rows.GroupBy(r => r.WorkDate).OrderBy(d => d.Key).Select(d => new PayrollMatrixCell(d.Key, d.Sum(r => r.Qty), d.Sum(r => r.Subtotal))).ToList();
            return Task.FromResult(new PayrollMatrixDto(dates, matrix, totals));
        }
    }

    public Task<IReadOnlyList<PayrollDailyRow>> GetAdjustmentsAsync(Guid periodId, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<PayrollDailyRow>>(RowsOf(Find(periodId)).Where(r => r.Kind == OutputKind.Adjustment).ToList()); }
    }

    // ----- Ekspor & slip -----

    public Task<FileContent> ExportAsync(Guid periodId, PayrollExportFormat format, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = Find(periodId);
            var status = p.Status == PayrollStatus.Open ? "BELUM FINAL" : "FINAL";
            var suffix = p.Status == PayrollStatus.Open ? "-BELUM-FINAL" : string.Empty;
            var sb = new StringBuilder("﻿");
            string name;
            if (format == PayrollExportFormat.CsvDaily)
            {
                sb.AppendLine("status,tanggal,kode_pekerja,pekerja,wo,proses,size,jenis,qty,tarif,subtotal");
                foreach (var r in RowsOf(p).OrderBy(r => r.WorkDate)) { sb.AppendLine(string.Join(',', status, r.WorkDate.ToString("yyyy-MM-dd"), Csv(r.WorkerCode), Csv(r.WorkerName), r.WoCode, Csv(r.ProcessName), r.SizeCode, r.Kind, r.Qty, r.Rate.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Subtotal.ToString(System.Globalization.CultureInfo.InvariantCulture))); }
                name = $"{CodeOf(p.Start, p.End)}-harian{suffix}.csv";
            }
            else
            {
                sb.AppendLine("status,kode_pekerja,pekerja,total_qty,jumlah,dibayarkan");
                foreach (var s in Summarize(RowsOf(p))) { sb.AppendLine(string.Join(',', status, Csv(s.WorkerCode), Csv(s.WorkerName), s.TotalQty, s.SumBeforeRounding.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Paid.ToString(System.Globalization.CultureInfo.InvariantCulture))); }
                name = $"{CodeOf(p.Start, p.End)}{(format == PayrollExportFormat.Xlsx ? string.Empty : "-ringkasan")}{suffix}.{(format == PayrollExportFormat.Xlsx ? "xlsx" : "csv")}";
            }

            var type = format == PayrollExportFormat.Xlsx ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/csv";
            return Task.FromResult(new FileContent(name, type, Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }

    /// <summary>Sel teks berawalan = + - @ diberi apostrof (cegah injeksi rumus), seperti API.</summary>
    private static string Csv(string value)
    {
        var safe = value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
        return safe.Contains(',') || safe.Contains('"') ? "\"" + safe.Replace("\"", "\"\"") + "\"" : safe;
    }

    public Task<FileContent> GetSlipsAsync(Guid periodId, Guid? workerId, bool includeDaily, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var p = Find(periodId);
            var rows = RowsOf(p).Where(r => workerId is null || r.WorkerId == workerId).ToList();
            if (rows.Count == 0) { throw Problem(ProblemTypes.NotFound, 404, "Pekerja tidak memiliki output pada periode ini."); }
            var text = $"%PDF-1.4\n% Slip {CodeOf(p.Start, p.End)} {(p.Status == PayrollStatus.Open ? "BELUM FINAL" : "FINAL")}{(includeDaily ? " + harian" : string.Empty)}\n%%EOF\n";
            return Task.FromResult(new FileContent($"slip-{CodeOf(p.Start, p.End)}{(workerId is null ? string.Empty : "-" + rows[0].WorkerCode)}.pdf", "application/pdf", Encoding.ASCII.GetBytes(text)));
        }
    }
}
