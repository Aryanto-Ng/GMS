using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Production;
using Gms.Shared.WorkOrders;

namespace Gms.Web.Services.Api;

/// <summary>
/// Produksi tiruan (ADR-014): output pekerja per langkah Internal (kumulatif ≤ output langkah sebelumnya; langkah pertama bebas tetapi > Qty WO perlu konfirmasi),
/// void/koreksi dengan pratinjau dampak terhadap langkah hilir, penyesuaian, serta kirim/terima vendor. Memutakhirkan <see cref="MockWoApi"/> (progres, status).
/// </summary>
public sealed class MockProductionApi : IProductionApi
{
    private sealed class Entry
    {
        public Guid Id = Guid.NewGuid(); public EntryKind Kind; public Guid WoId; public int Seq; public Guid? WorkerId, VendorId; public DateOnly Date; public Dictionary<Guid, int> Lines = [];
        public DocStatus Status; public string? VoidReason, Reason, DocNo, Notes; public Guid? ReplacesId, ReplacedById; public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow; public DateOnly? OriginalDate;
    }

    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly MockWoApi _wos;
    private readonly MockWorkerApi _workers;
    private readonly MockVendorApi _vendors;

    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    /// <summary>Tanggal sebelum atau pada nilai ini dianggap periode gaji tertutup (diatur modul Payroll tiruan). Null = tidak ada.</summary>
    public Func<DateOnly?> ClosedUntil { get; set; } = () => null;

    public MockProductionApi(MockWoApi wos, MockWorkerApi workers, MockVendorApi vendors)
    {
        _wos = wos;
        _workers = workers;
        _vendors = vendors;
    }

    private static ApiException Problem(string type, int status, string title, string? detail = null, string? code = null, IReadOnlyList<RuleViolation>? violations = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, Code = code, Violations = violations, Errors = errors, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    private static ApiException Invalid(string path, string message) =>
        Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: new Dictionary<string, string[]> { [path] = [message] });

    private WoDto Wo(Guid id) => _wos.GetAsync(id).GetAwaiter().GetResult();

    private IReadOnlyList<WoStepProgress> Steps(Guid woId) => _wos.GetProgressAsync(woId).GetAwaiter().GetResult().Steps;

    private static string Label(ColorSizeRef c) => $"{c.ColorName} · {c.SizeCode}";

    /// <summary>Kumulatif aktif suatu langkah Internal pada satu baris WO (Output + Penyesuaian).</summary>
    private int Cum(Guid woId, int seq, Guid lineId) =>
        _entries.Where(e => e.WoId == woId && e.Seq == seq && e.Status == DocStatus.Active && e.Kind is EntryKind.Output or EntryKind.Adjustment).Sum(e => e.Lines.GetValueOrDefault(lineId));

    private int Received(Guid woId, int seq, Guid lineId, Guid? vendor = null) =>
        _entries.Where(e => e.WoId == woId && e.Seq == seq && e.Status == DocStatus.Active && e.Kind == EntryKind.VendorReceive && (vendor is null || e.VendorId == vendor)).Sum(e => e.Lines.GetValueOrDefault(lineId));

    private int SentQty(Guid woId, int seq, Guid lineId, Guid? vendor = null) =>
        _entries.Where(e => e.WoId == woId && e.Seq == seq && e.Status == DocStatus.Active && e.Kind == EntryKind.VendorSend && (vendor is null || e.VendorId == vendor)).Sum(e => e.Lines.GetValueOrDefault(lineId));

    /// <summary>"Output" suatu langkah untuk langkah berikutnya: Internal = kumulatif; Vendor = total diterima.</summary>
    private int StepOutput(Guid woId, WoStepProgress step, Guid lineId) =>
        step.ProcessType == ProcessType.Vendor ? Received(woId, step.Seq, lineId) : Cum(woId, step.Seq, lineId);

    private static int IndexOf(IReadOnlyList<WoStepProgress> steps, int seq) => steps.ToList().FindIndex(s => s.Seq == seq);

    // ----- Input output -----

    public Task<InputContextDto> GetInputContextAsync(Guid woId, int seq, DateOnly workDate, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Wo(woId);
            var steps = Steps(woId);
            var idx = IndexOf(steps, seq);
            if (idx < 0 || steps[idx].ProcessType != ProcessType.Internal) { throw Invalid("seq", "Langkah harus bertipe Internal pada routing WO."); }
            var first = idx == 0;
            var blocked = wo.Status is WoStatus.Released or WoStatus.InProgress ? null : $"WO ini berstatus {wo.Status}. Buka kembali untuk menginput.";
            if (blocked is null && workDate > Today()) { blocked = "Tanggal kerja tidak boleh di masa depan."; }
            if (blocked is null && ClosedUntil() is { } closed && workDate <= closed) { blocked = "Tanggal kerja berada pada periode gaji tertutup. Gunakan Penyesuaian."; }
            var cols = wo.Lines.Select(l => new InputColumn(l.PoLineId, l.ColorSize, first ? null : Math.Max(0, StepOutput(woId, steps[idx - 1], l.PoLineId) - Cum(woId, seq, l.PoLineId)), l.Qty, Cum(woId, seq, l.PoLineId))).ToList();
            var prevTotal = first ? 0 : wo.Lines.Sum(l => StepOutput(woId, steps[idx - 1], l.PoLineId));
            var current = wo.Lines.Sum(l => Cum(woId, seq, l.PoLineId));
            return Task.FromResult(new InputContextDto(woId, wo.Code, seq, steps[idx].ProcessName, workDate, prevTotal, current, first ? wo.TotalQty - current : prevTotal - current, first, cols, [], blocked));
        }
    }

    private void CheckInputAllowed(Guid woId, int seq, DateOnly date, bool adjustment = false)
    {
        var wo = Wo(woId);
        if (wo.Status is not (WoStatus.Released or WoStatus.InProgress))
        {
            throw Problem(ProblemTypes.StateConflict, 409, "Status WO tidak mengizinkan input.", $"WO ini berstatus {wo.Status}.", ProblemCodes.WoStatusInvalid);
        }

        var steps = Steps(woId);
        var idx = IndexOf(steps, seq);
        if (idx < 0 || steps[idx].ProcessType != ProcessType.Internal) { throw Invalid("seq", "Output hanya untuk langkah Internal pada routing WO."); }
        if (date > Today()) { throw Invalid("workDate", "Tanggal kerja tidak boleh di masa depan."); }
        if (!adjustment && ClosedUntil() is { } closed && date <= closed)
        {
            throw Problem(ProblemTypes.PeriodClosed, 409, "Tanggal kerja berada pada periode gaji tertutup.", "Gunakan Penyesuaian di periode berikutnya.");
        }
    }

    /// <summary>Batas bawah suatu langkah karena langkah hilir: Internal = kumulatif hilir; Vendor = total terkirim ke vendor.</summary>
    private int NextLimit(Guid woId, WoStepProgress next, Guid lineId) =>
        next.ProcessType == ProcessType.Vendor ? SentQty(woId, next.Seq, lineId) : Cum(woId, next.Seq, lineId);

    /// <summary>Memeriksa aturan kumulatif setelah perubahan hipotetis <paramref name="delta"/> (per baris WO) pada langkah <paramref name="seq"/>.</summary>
    private (List<ImpactRow> Rows, List<RuleViolation> Violations) Impact(Guid woId, int seq, IReadOnlyDictionary<Guid, int> delta)
    {
        var wo = Wo(woId);
        var steps = Steps(woId);
        var idx = IndexOf(steps, seq);
        var rows = new List<ImpactRow>();
        var violations = new List<RuleViolation>();
        foreach (var l in wo.Lines.Where(l => delta.GetValueOrDefault(l.PoLineId) != 0))
        {
            var before = Cum(woId, seq, l.PoLineId);
            var after = before + delta[l.PoLineId];
            var subject = $"{steps[idx].ProcessName} · {Label(l.ColorSize)}";
            string? message = null;
            decimal? limit = null;
            if (after < 0) { message = "Kumulatif tidak boleh negatif."; limit = 0; }
            else if (idx > 0 && after > StepOutput(woId, steps[idx - 1], l.PoLineId))
            {
                limit = StepOutput(woId, steps[idx - 1], l.PoLineId);
                message = $"Melebihi output langkah sebelumnya ({limit}).";
            }
            else if (idx + 1 < steps.Count && after < NextLimit(woId, steps[idx + 1], l.PoLineId))
            {
                limit = NextLimit(woId, steps[idx + 1], l.PoLineId);
                message = $"Tertahan oleh {steps[idx + 1].ProcessName} ({limit}).";
            }

            var ok = message is null;
            rows.Add(new ImpactRow(subject, $"Kumulatif langkah {seq}", before, after, limit, ok, message));
            if (!ok) { violations.Add(new RuleViolation(idx + 1 < steps.Count && after < (limit ?? 0) ? ProblemCodes.OutputBelowDownstream : ProblemCodes.OutputCumulativeExceeded, subject, after, limit, message!)); }
        }

        return (rows, violations);
    }

    public Task<SaveOutputsResult> SaveOutputsAsync(SaveOutputsRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            CheckInputAllowed(r.WoId, r.Seq, r.WorkDate);
            if (r.Rows is null || r.Rows.Count == 0) { throw Invalid("rows", "Tambahkan minimal satu pekerja."); }
            if (r.Rows.Select(x => x.WorkerId).Distinct().Count() != r.Rows.Count) { throw Invalid("rows", "Pekerja tidak boleh dobel."); }
            var delta = new Dictionary<Guid, int>();
            for (var i = 0; i < r.Rows.Count; i++)
            {
                if (!_workers.Contains(r.Rows[i].WorkerId)) { throw Invalid($"rows[{i}].workerId", "Pekerja tidak ditemukan."); }
                foreach (var c in r.Rows[i].Cells.Where(c => c.Qty != 0))
                {
                    if (c.Qty < 0) { throw Invalid($"rows[{i}].cells", "Qty tidak boleh negatif."); }
                    delta[c.WoLineId] = delta.GetValueOrDefault(c.WoLineId) + c.Qty;
                }
            }

            if (delta.Count == 0) { throw Invalid("rows", "Isi minimal satu Qty."); }
            var steps = Steps(r.WoId);
            var idx = IndexOf(steps, r.Seq);
            var (_, violations) = Impact(r.WoId, r.Seq, delta);
            var blocking = violations.Where(v => v.Code == ProblemCodes.OutputCumulativeExceeded).ToList();
            if (blocking.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Output melebihi batas langkah sebelumnya.", code: ProblemCodes.OutputCumulativeExceeded, violations: blocking); }

            var wo = Wo(r.WoId);
            var warnings = new List<string>();
            if (idx == 0)
            {
                var over = wo.Lines.Where(l => Cum(r.WoId, r.Seq, l.PoLineId) + delta.GetValueOrDefault(l.PoLineId) > l.Qty).ToList();
                if (over.Count > 0)
                {
                    var l = over[0];
                    var text = $"Melebihi Qty WO (kumulatif {Cum(r.WoId, r.Seq, l.PoLineId) + delta[l.PoLineId]} dari {l.Qty})";
                    if (!r.ConfirmExceedWoQty)
                    {
                        throw Problem(ProblemTypes.RuleViolation, 409, "Output melebihi Qty WO.", text + ". Konfirmasi untuk melanjutkan.", ProblemCodes.OutputExceedsWoQtyNeedsConfirmation,
                            [new RuleViolation(ProblemCodes.OutputExceedsWoQtyNeedsConfirmation, Label(l.ColorSize), Cum(r.WoId, r.Seq, l.PoLineId) + delta[l.PoLineId], l.Qty, text)]);
                    }

                    warnings.Add(text);
                }
            }

            var ids = new List<Guid>();
            foreach (var row in r.Rows.Where(x => x.Cells.Any(c => c.Qty > 0)))
            {
                var e = new Entry { Kind = EntryKind.Output, WoId = r.WoId, Seq = r.Seq, WorkerId = row.WorkerId, Date = r.WorkDate, Status = DocStatus.Active };
                foreach (var c in row.Cells.Where(c => c.Qty > 0)) { e.Lines[c.WoLineId] = e.Lines.GetValueOrDefault(c.WoLineId) + c.Qty; }
                _entries.Add(e);
                ids.Add(e.Id);
                foreach (var (line, qty) in e.Lines) { _wos.AddOutput(r.WoId, r.Seq, line, qty); }
            }

            return Task.FromResult(new SaveOutputsResult(ids, delta.Values.Sum(), ids.Count, warnings));
        }
    }

    // ----- Riwayat -----

    private (bool Void, bool Correct, string? Disabled) Allowed(Entry e)
    {
        if (e.Status == DocStatus.Voided) { return (false, false, null); }
        var wo = Wo(e.WoId);
        if (wo.Status is WoStatus.Completed or WoStatus.Closed or WoStatus.Cancelled) { return (false, false, $"WO berstatus {wo.Status}."); }
        if (ClosedUntil() is { } closed && e.Date <= closed) { return (false, false, "Tanggal kerja berada pada periode gaji tertutup. Gunakan Penyesuaian."); }
        return (true, e.Kind == EntryKind.Output, null);
    }

    private EntryListItem Item(Entry e)
    {
        var wo = Wo(e.WoId);
        var step = Steps(e.WoId).FirstOrDefault(s => s.Seq == e.Seq);
        var (canVoid, canCorrect, disabled) = Allowed(e);
        return new EntryListItem(e.Id, e.Kind, e.WoId, wo.Code, e.Seq, step?.ProcessName ?? $"#{e.Seq}", e.WorkerId, e.WorkerId is { } w ? _workers.GetAsync(w).GetAwaiter().GetResult().Name : null,
            e.VendorId, e.VendorId is { } v ? _vendors.GetAsync(v).GetAwaiter().GetResult().Name : null, e.Date, e.Lines.Values.Sum(), e.Status, e.CreatedAt, "Administrator", e.VoidReason,
            e.ReplacesId, e.ReplacedById, canVoid, canCorrect, disabled);
    }

    public Task<PagedResult<EntryListItem>> ListEntriesAsync(EntryListQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<Entry> rows = _entries;
            if (q.WoId is { } wo) { rows = rows.Where(e => e.WoId == wo); }
            if (q.Seq is { } seq) { rows = rows.Where(e => e.Seq == seq); }
            if (q.WorkerId is { } w) { rows = rows.Where(e => e.WorkerId == w); }
            if (q.VendorId is { } v) { rows = rows.Where(e => e.VendorId == v); }
            if (q.From is { } f) { rows = rows.Where(e => e.Date >= f); }
            if (q.To is { } t) { rows = rows.Where(e => e.Date <= t); }
            if (q.Kind is { } k) { rows = rows.Where(e => e.Kind == k); }
            if (q.HariIni) { rows = rows.Where(e => e.Date == Today()); }
            if (q.Status is { } st) { rows = rows.Where(e => e.Status == st); }
            else if (!q.IncludeVoided) { rows = rows.Where(e => e.Status == DocStatus.Active); }
            var all = rows.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Date).ToList();
            return Task.FromResult(new PagedResult<EntryListItem>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(Item).ToList(), q.Page, q.PageSize, all.Count));
        }
    }

    private Entry Find(Guid id) => _entries.FirstOrDefault(e => e.Id == id) ?? throw Problem(ProblemTypes.NotFound, 404, "Entri tidak ditemukan.");

    public Task<EntryDetailDto> GetEntryAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var e = Find(id);
            var wo = Wo(e.WoId);
            var lines = e.Lines.Select(l => new EntryLineDto(l.Key, wo.Lines.First(x => x.PoLineId == l.Key).ColorSize, l.Value)).ToList();
            return Task.FromResult(new EntryDetailDto(Item(e), lines, e.Reason, e.OriginalDate, null, e.DocNo, e.Notes));
        }
    }

    // ----- Void / koreksi -----

    private ImpactPreview BuildPreview(IEnumerable<ImpactRow> rows, List<RuleViolation> violations) => new([.. rows], violations.Count == 0, violations, []);

    public Task<ImpactPreview> PreviewVoidAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var e = Find(id);
            if (e.Kind == EntryKind.Output || e.Kind == EntryKind.Adjustment)
            {
                var (rows, violations) = Impact(e.WoId, e.Seq, e.Lines.ToDictionary(l => l.Key, l => -l.Value));
                return Task.FromResult(BuildPreview(rows, violations));
            }

            return PreviewVoidVendorMovementAsync(id, ct);
        }
    }

    public Task VoidAsync(Guid id, ReasonRequest request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var e = Find(id);
            DoVoid(e, request.Reason);
            return Task.CompletedTask;
        }
    }

    private void DoVoid(Entry e, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
        if (e.Status == DocStatus.Voided) { throw Problem(ProblemTypes.StateConflict, 409, "Entri sudah di-void."); }
        var (canVoid, _, disabled) = Allowed(e);
        if (!canVoid) { throw Problem(ProblemTypes.StateConflict, 409, "Entri tidak dapat di-void.", disabled, ProblemCodes.WoStatusInvalid); }
        if (e.Kind is EntryKind.Output or EntryKind.Adjustment)
        {
            var (_, violations) = Impact(e.WoId, e.Seq, e.Lines.ToDictionary(l => l.Key, l => -l.Value));
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Void ditolak oleh langkah hilir.", code: ProblemCodes.OutputBelowDownstream, violations: violations); }
            foreach (var (line, qty) in e.Lines) { _wos.AddOutput(e.WoId, e.Seq, line, -qty); }
        }
        else
        {
            var (_, violations) = VendorImpact(e, new Dictionary<Guid, int>());
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Void ditolak.", code: violations[0].Code, violations: violations); }
            ApplyVendorToWo(e, -1);
        }

        e.Status = DocStatus.Voided;
        e.VoidReason = reason.Trim();
    }

    private (List<ImpactRow> Rows, List<RuleViolation> Violations) CorrectionImpact(Entry e, CorrectOutputRequest r)
    {
        var delta = new Dictionary<Guid, int>();
        foreach (var (line, qty) in e.Lines) { delta[line] = delta.GetValueOrDefault(line) - qty; }
        foreach (var c in r.Cells) { delta[c.WoLineId] = delta.GetValueOrDefault(c.WoLineId) + c.Qty; }
        return Impact(e.WoId, e.Seq, delta);
    }

    public Task<ImpactPreview> PreviewCorrectOutputAsync(Guid id, CorrectOutputRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var e = Find(id);
            var (rows, violations) = CorrectionImpact(e, r);
            return Task.FromResult(BuildPreview(rows, violations));
        }
    }

    public Task<Guid> CorrectOutputAsync(Guid id, CorrectOutputRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var e = Find(id);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            var (_, canCorrect, disabled) = Allowed(e);
            if (!canCorrect) { throw Problem(ProblemTypes.StateConflict, 409, "Entri tidak dapat dikoreksi.", disabled, ProblemCodes.WoStatusInvalid); }
            CheckInputAllowed(e.WoId, e.Seq, r.WorkDate);
            if (!_workers.Contains(r.WorkerId)) { throw Invalid("workerId", "Pekerja tidak ditemukan."); }
            var (_, violations) = CorrectionImpact(e, r);
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Koreksi ditolak.", code: violations[0].Code, violations: violations); }
            foreach (var (line, qty) in e.Lines) { _wos.AddOutput(e.WoId, e.Seq, line, -qty); }
            e.Status = DocStatus.Voided;
            e.VoidReason = r.Reason.Trim();
            var replacement = new Entry { Kind = EntryKind.Output, WoId = e.WoId, Seq = e.Seq, WorkerId = r.WorkerId, Date = r.WorkDate, Status = DocStatus.Active, ReplacesId = e.Id, Reason = r.Reason.Trim() };
            foreach (var c in r.Cells.Where(c => c.Qty > 0)) { replacement.Lines[c.WoLineId] = c.Qty; _wos.AddOutput(e.WoId, e.Seq, c.WoLineId, c.Qty); }
            e.ReplacedById = replacement.Id;
            _entries.Add(replacement);
            return Task.FromResult(replacement.Id);
        }
    }

    public Task<Guid> CreateAdjustmentAsync(AdjustmentRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            CheckInputAllowed(r.WoId, r.Seq, r.WorkDate, adjustment: true);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            if (!_workers.Contains(r.WorkerId)) { throw Invalid("workerId", "Pekerja tidak ditemukan."); }
            if (ClosedUntil() is not { } closed || r.OriginalDate > closed)
            {
                throw Invalid("originalDate", "Tanggal asal harus berada pada periode gaji tertutup. Gunakan Void atau Koreksi dari Riwayat.");
            }

            var delta = r.Cells.Where(c => c.Qty != 0).ToDictionary(c => c.WoLineId, c => c.Qty);
            if (delta.Count == 0) { throw Invalid("cells", "Isi minimal satu Qty."); }
            var (_, violations) = Impact(r.WoId, r.Seq, delta);
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Penyesuaian melanggar aturan kumulatif.", code: violations[0].Code, violations: violations); }
            var e = new Entry { Kind = EntryKind.Adjustment, WoId = r.WoId, Seq = r.Seq, WorkerId = r.WorkerId, Date = r.WorkDate, OriginalDate = r.OriginalDate, Reason = r.Reason.Trim(), Status = DocStatus.Active };
            foreach (var (line, qty) in delta) { e.Lines[line] = qty; _wos.AddOutput(r.WoId, r.Seq, line, qty); }
            _entries.Add(e);
            return Task.FromResult(e.Id);
        }
    }

    // ----- Vendor -----

    private void ApplyVendorToWo(Entry e, int sign)
    {
        var qty = e.Lines.Values.Sum() * sign;
        if (e.Kind == EntryKind.VendorSend) { _wos.SetVendor(e.WoId, e.Seq, qty, 0); } else { _wos.SetVendor(e.WoId, e.Seq, 0, qty); }
    }

    public Task<VendorContextDto> GetVendorContextAsync(Guid woId, int seq, Guid? vendorId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Wo(woId);
            var steps = Steps(woId);
            var idx = IndexOf(steps, seq);
            if (idx < 0 || steps[idx].ProcessType != ProcessType.Vendor) { throw Invalid("seq", "Langkah harus bertipe Vendor pada routing WO."); }
            var blocked = wo.Status is WoStatus.Released or WoStatus.InProgress ? null : $"WO ini berstatus {wo.Status}. Buka kembali untuk menginput.";
            var cols = wo.Lines.Select(l =>
            {
                var available = idx == 0 ? l.Qty : StepOutput(woId, steps[idx - 1], l.PoLineId);
                return new VendorColumn(l.PoLineId, l.ColorSize, Math.Max(0, available - SentQty(woId, seq, l.PoLineId)),
                    vendorId is null ? 0 : Math.Max(0, SentQty(woId, seq, l.PoLineId, vendorId) - Received(woId, seq, l.PoLineId, vendorId)), SentQty(woId, seq, l.PoLineId), Received(woId, seq, l.PoLineId));
            }).ToList();
            var vendorSummary = _entries.Where(e => e.WoId == woId && e.Seq == seq && e.Status == DocStatus.Active && e.VendorId is not null).GroupBy(e => e.VendorId!.Value).Select(g =>
            {
                var sent = g.Where(e => e.Kind == EntryKind.VendorSend).Sum(e => e.Lines.Values.Sum());
                var received = g.Where(e => e.Kind == EntryKind.VendorReceive).Sum(e => e.Lines.Values.Sum());
                return new VendorSummary(g.Key, _vendors.GetAsync(g.Key).GetAwaiter().GetResult().Name, sent, received, sent - received);
            }).ToList();
            return Task.FromResult(new VendorContextDto(woId, wo.Code, seq, steps[idx].ProcessName, vendorId, cols, vendorSummary, blocked));
        }
    }

    /// <summary>Pemeriksaan batas vendor untuk entri <paramref name="e"/> bila diganti dengan <paramref name="replacement"/> (kosong = di-void).</summary>
    private (List<ImpactRow> Rows, List<RuleViolation> Violations) VendorImpact(Entry e, IReadOnlyDictionary<Guid, int> replacement)
    {
        var wo = Wo(e.WoId);
        var steps = Steps(e.WoId);
        var idx = IndexOf(steps, e.Seq);
        var rows = new List<ImpactRow>();
        var violations = new List<RuleViolation>();
        foreach (var l in wo.Lines.Where(l => e.Lines.ContainsKey(l.PoLineId) || replacement.ContainsKey(l.PoLineId)))
        {
            var oldQty = e.Lines.GetValueOrDefault(l.PoLineId);
            var newQty = replacement.GetValueOrDefault(l.PoLineId);
            var sent = SentQty(e.WoId, e.Seq, l.PoLineId, e.VendorId);
            var received = Received(e.WoId, e.Seq, l.PoLineId, e.VendorId);
            var subject = $"{steps[idx].ProcessName} · {Label(l.ColorSize)}";
            string? message = null;
            decimal? limit = null;
            if (e.Kind == EntryKind.VendorSend)
            {
                var after = sent - oldQty + newQty;
                if (after < received) { limit = received; message = $"Sudah diterima {received} dari vendor."; }
                rows.Add(new ImpactRow(subject, "Terkirim", sent, after, limit, message is null, message));
            }
            else
            {
                var after = received - oldQty + newQty;
                if (after > sent) { limit = sent; message = $"Melebihi yang terkirim ke vendor ({sent})."; }
                else if (idx + 1 < steps.Count && after < StepOutput(e.WoId, steps[idx + 1], l.PoLineId)) { limit = StepOutput(e.WoId, steps[idx + 1], l.PoLineId); message = $"Tertahan oleh {steps[idx + 1].ProcessName} ({limit})."; }
                rows.Add(new ImpactRow(subject, "Diterima", received, after, limit, message is null, message));
            }

            if (message is not null) { violations.Add(new RuleViolation(e.Kind == EntryKind.VendorSend ? ProblemCodes.VendorSentExceeded : ProblemCodes.VendorReceivedExceeded, subject, null, limit, message)); }
        }

        return (rows, violations);
    }

    public Task<Guid> SaveVendorMovementAsync(SaveVendorMovementRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Wo(r.WoId);
            if (wo.Status is not (WoStatus.Released or WoStatus.InProgress)) { throw Problem(ProblemTypes.StateConflict, 409, "Status WO tidak mengizinkan input.", code: ProblemCodes.WoStatusInvalid); }
            var steps = Steps(r.WoId);
            var idx = IndexOf(steps, r.Seq);
            if (idx < 0 || steps[idx].ProcessType != ProcessType.Vendor) { throw Invalid("seq", "Kirim/terima hanya untuk langkah Vendor pada routing WO."); }
            if (!_vendors.Contains(r.VendorId)) { throw Invalid("vendorId", "Vendor wajib dipilih."); }
            if (r.Date > Today()) { throw Invalid("date", "Tanggal tidak boleh di masa depan."); }
            var cells = r.Cells.Where(c => c.Qty > 0).ToList();
            if (cells.Count == 0) { throw Invalid("cells", "Isi minimal satu Qty."); }
            var violations = new List<RuleViolation>();
            foreach (var c in cells)
            {
                var l = wo.Lines.First(x => x.PoLineId == c.WoLineId);
                var available = idx == 0 ? l.Qty : StepOutput(r.WoId, steps[idx - 1], l.PoLineId);
                var max = r.Kind == VendorMovementKind.Send ? available - SentQty(r.WoId, r.Seq, l.PoLineId) : SentQty(r.WoId, r.Seq, l.PoLineId, r.VendorId) - Received(r.WoId, r.Seq, l.PoLineId, r.VendorId);
                if (c.Qty > max) { violations.Add(new RuleViolation(r.Kind == VendorMovementKind.Send ? ProblemCodes.VendorSentExceeded : ProblemCodes.VendorReceivedExceeded, Label(l.ColorSize), c.Qty, max, $"Maksimal {Math.Max(0, max)}.")); }
            }

            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, r.Kind == VendorMovementKind.Send ? "Kirim melebihi batas." : "Terima melebihi batas.", code: violations[0].Code, violations: violations); }
            var e = new Entry
            {
                Kind = r.Kind == VendorMovementKind.Send ? EntryKind.VendorSend : EntryKind.VendorReceive, WoId = r.WoId, Seq = r.Seq, VendorId = r.VendorId, Date = r.Date,
                DocNo = string.IsNullOrWhiteSpace(r.DocNo) ? null : r.DocNo.Trim(), Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(), Status = DocStatus.Active,
            };
            foreach (var c in cells) { e.Lines[c.WoLineId] = c.Qty; }
            _entries.Add(e);
            ApplyVendorToWo(e, 1);
            if (r.Kind == VendorMovementKind.Receive) { _wos.AddOutput(r.WoId, r.Seq, cells[0].WoLineId, 0); }
            return Task.FromResult(e.Id);
        }
    }

    public Task<ImpactPreview> PreviewVoidVendorMovementAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var (rows, violations) = VendorImpact(Find(id), new Dictionary<Guid, int>());
            return Task.FromResult(BuildPreview(rows, violations));
        }
    }

    public Task VoidVendorMovementAsync(Guid id, ReasonRequest request, CancellationToken ct = default)
    {
        lock (_gate) { DoVoid(Find(id), request.Reason); return Task.CompletedTask; }
    }

    public Task<ImpactPreview> PreviewCorrectVendorMovementAsync(Guid id, CorrectVendorMovementRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var (rows, violations) = VendorImpact(Find(id), r.Cells.Where(c => c.Qty > 0).ToDictionary(c => c.WoLineId, c => c.Qty));
            return Task.FromResult(BuildPreview(rows, violations));
        }
    }

    public Task<Guid> CorrectVendorMovementAsync(Guid id, CorrectVendorMovementRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var e = Find(id);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            var replacementCells = r.Cells.Where(c => c.Qty > 0).ToDictionary(c => c.WoLineId, c => c.Qty);
            var (_, violations) = VendorImpact(e, replacementCells);
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Koreksi ditolak.", code: violations[0].Code, violations: violations); }
            ApplyVendorToWo(e, -1);
            e.Status = DocStatus.Voided;
            e.VoidReason = r.Reason.Trim();
            var n = new Entry { Kind = e.Kind, WoId = e.WoId, Seq = e.Seq, VendorId = r.VendorId, Date = r.Date, DocNo = r.DocNo, Notes = r.Notes, Status = DocStatus.Active, ReplacesId = e.Id, Reason = r.Reason.Trim(), Lines = replacementCells };
            e.ReplacedById = n.Id;
            _entries.Add(n);
            ApplyVendorToWo(n, 1);
            return Task.FromResult(n.Id);
        }
    }

    /// <summary>Sumber upah: entri Output/Penyesuaian aktif per baris WO (Payroll tiruan).</summary>
    public IReadOnlyList<WageSource> WageSources()
    {
        lock (_gate)
        {
            return _entries.Where(e => e.Status == DocStatus.Active && e.Kind is EntryKind.Output or EntryKind.Adjustment && e.WorkerId is not null)
                .SelectMany(e => e.Lines.Select(l => new WageSource(e.Id, e.Date, e.WorkerId!.Value, e.WoId, e.Seq, l.Key, e.Kind == EntryKind.Output ? OutputKind.Output : OutputKind.Adjustment, l.Value, e.OriginalDate, e.Reason))).ToList();
        }
    }

    /// <summary>Total output aktif (Internal) langkah terakhir per baris WO, untuk modul Serah Terima.</summary>
    public int LastStepOutput(Guid woId, Guid lineId)
    {
        lock (_gate)
        {
            var steps = Steps(woId);
            return steps.Count == 0 ? 0 : StepOutput(woId, steps[^1], lineId);
        }
    }
}

/// <summary>Satu baris sumber upah (entri × baris WO).</summary>
public sealed record WageSource(Guid EntryId, DateOnly Date, Guid WorkerId, Guid WoId, int Seq, Guid WoLineId, OutputKind Kind, int Qty, DateOnly? OriginalDate, string? Reason);
