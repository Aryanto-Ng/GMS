using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.WorkOrders;

namespace Gms.Web.Services.Api;

/// <summary>
/// Work Order tiruan (ADR-014): Draft→Released→InProgress→Completed→Closed/Cancelled dengan aturan API (FG siap, Qty ≤ Sisa Rencana PO, snapshot routing+tarif saat Rilis,
/// batas minimum Qty = output proses pertama, Cancel hanya tanpa aktivitas). Output/serah terima diisi modul lain lewat pengait (<see cref="AddOutput"/> dst.).
/// </summary>
public sealed class MockWoApi : IWorkOrdersApi
{
    private sealed class Wo
    {
        public Guid Id = Guid.NewGuid(); public string Code = ""; public Guid PoId; public Guid FgId; public WoStatus Status; public DateOnly? Start, Target; public string? Notes, CancelReason;
        public List<(Guid PoLineId, int Qty)> Lines = []; public DateTimeOffset? ReleasedAt; public uint Version = 1;
        public List<RoutingStepDto> Routing = []; public Dictionary<(Guid Process, Guid Size), decimal> Tariffs = []; public List<SizeRef> TariffSizes = [];
        public List<WoStatusHistoryItem> History = [];
    }

    private readonly object _gate = new();
    private readonly List<Wo> _wos = [];
    private readonly Dictionary<(Guid Wo, int Seq, Guid Line), int> _output = [];
    private readonly Dictionary<(Guid Wo, int Seq), (int Sent, int Received)> _vendor = [];
    private readonly Dictionary<(Guid Wo, Guid Line), int> _handedOver = [];
    private readonly MockPoApi _pos;
    private readonly MockFgApi _fgs;
    private readonly MockCustomerApi _customers;

    public MockWoApi(MockPoApi pos, MockFgApi fgs, MockCustomerApi customers)
    {
        _pos = pos;
        _fgs = fgs;
        _customers = customers;
        SeedDemo();
    }

    public static readonly Guid SeedWoId = Guid.Parse("00000000-0000-0000-0000-00000000d001");

    /// <summary>WO contoh (Released) untuk PO awal, agar layar progres dan output punya data saat pengembangan UI.</summary>
    private void SeedDemo()
    {
        var po = _pos.GetAsync(MockPoApi.SeedPoId).Result;
        var wo = new Wo { Id = SeedWoId, Code = NextCode(), PoId = po.Id, FgId = MockFgApi.KemejaId, Start = Today(), Target = Today().AddDays(10), Lines = po.Lines.Take(2).Select(l => (l.Id, l.Qty / 2)).ToList() };
        wo.History.Add(new WoStatusHistoryItem(null, WoStatus.Draft, null, DateTimeOffset.UtcNow, "Administrator"));
        _wos.Add(wo);
        ReleaseAsync(wo.Id, new WoActionRequest(null, wo.Version)).GetAwaiter().GetResult();
    }

    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    // ----- Pengait modul lain -----

    public void AddOutput(Guid woId, int seq, Guid woLineId, int delta)
    {
        lock (_gate)
        {
            var wo = Find(woId);
            _output[(woId, seq, woLineId)] = _output.GetValueOrDefault((woId, seq, woLineId)) + delta;
            if (wo.Status == WoStatus.Released && delta > 0) { Move(wo, WoStatus.InProgress, null); }
            wo.Version++;
        }
    }

    public void SetVendor(Guid woId, int seq, int sentDelta, int receivedDelta)
    {
        lock (_gate) { var v = _vendor.GetValueOrDefault((woId, seq)); _vendor[(woId, seq)] = (v.Sent + sentDelta, v.Received + receivedDelta); }
    }

    public void AddHandedOver(Guid woId, Guid woLineId, int delta)
    {
        lock (_gate) { _handedOver[(woId, woLineId)] = _handedOver.GetValueOrDefault((woId, woLineId)) + delta; Find(woId).Version++; }
    }

    public IReadOnlyList<(Guid WoLineId, Guid PoLineId, int Qty, int Output, int HandedOver)> LinesOf(Guid woId)
    {
        lock (_gate)
        {
            var wo = Find(woId);
            var last = wo.Routing.Count == 0 ? 0 : wo.Routing[^1].Seq;
            return wo.Lines.Select(l => (l.PoLineId, l.PoLineId, l.Qty, _output.GetValueOrDefault((woId, last, l.PoLineId)), _handedOver.GetValueOrDefault((woId, l.PoLineId)))).ToList();
        }
    }

    /// <summary>Tarif snapshot per langkah+size (Payroll memakai tarif snapshot WO).</summary>
    public decimal? RateFor(Guid woId, int seq, Guid sizeId)
    {
        lock (_gate)
        {
            var wo = Find(woId);
            var step = wo.Routing.FirstOrDefault(s => s.Seq == seq);
            return step is not null && wo.Tariffs.TryGetValue((step.ProcessId, sizeId), out var rate) ? rate : null;
        }
    }

    public IReadOnlyList<WoListItem> ActiveForOutput()
    {
        lock (_gate) { return _wos.Where(w => w.Status is WoStatus.Released or WoStatus.InProgress).Select(ListItem).ToList(); }
    }

    // ----- Util -----

    private static ApiException Problem(string type, int status, string title, string? detail = null, string? code = null, IReadOnlyList<RuleViolation>? violations = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, Code = code, Violations = violations, Errors = errors, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    private static ApiException Invalid(string path, string message) =>
        Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: new Dictionary<string, string[]> { [path] = [message] });

    private Wo Find(Guid id) => _wos.FirstOrDefault(w => w.Id == id) ?? throw Problem(ProblemTypes.NotFound, 404, "WO tidak ditemukan.");

    private static void CheckVersion(Wo wo, uint v)
    {
        if (wo.Version != v) { throw Problem(ProblemTypes.ConcurrencyConflict, 409, "Status WO telah berubah.", "Muat ulang halaman lalu ulangi."); }
    }

    private static void RequireState(Wo wo, params WoStatus[] allowed)
    {
        if (!allowed.Contains(wo.Status)) { throw Problem(ProblemTypes.StateConflict, 409, "Status WO tidak mengizinkan aksi ini.", $"WO berstatus {wo.Status}."); }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Move(Wo wo, WoStatus to, string? reason)
    {
        wo.History.Add(new WoStatusHistoryItem(wo.History.Count == 0 ? null : wo.Status, to, reason, DateTimeOffset.UtcNow, "Administrator"));
        wo.Status = to;
    }

    private PoLineDto PoLine(Guid poId, Guid poLineId) =>
        _pos.GetAsync(poId).Result.Lines.FirstOrDefault(l => l.Id == poLineId) ?? throw Invalid("lines", "Baris PO tidak ditemukan.");

    private int FirstOutput(Wo wo, Guid poLineId) =>
        wo.Routing.Count == 0 ? 0 : _output.GetValueOrDefault((wo.Id, wo.Routing[0].Seq, poLineId));

    private int LastOutput(Wo wo) =>
        wo.Routing.Count == 0 ? 0 : wo.Lines.Sum(l => _output.GetValueOrDefault((wo.Id, wo.Routing[^1].Seq, l.PoLineId)));

    private bool HasActivity(Wo wo) =>
        _output.Any(o => o.Key.Wo == wo.Id && o.Value > 0) || _vendor.Any(v => v.Key.Wo == wo.Id && v.Value.Sent > 0) || _handedOver.Any(h => h.Key.Wo == wo.Id && h.Value > 0);

    private static IReadOnlyList<string> Actions(Wo wo) => wo.Status switch
    {
        WoStatus.Draft => [WoActions.Edit, WoActions.Release, WoActions.Cancel],
        WoStatus.Released => [WoActions.EditQuantities, WoActions.Cancel, WoActions.InputOutput],
        WoStatus.InProgress => [WoActions.EditQuantities, WoActions.Complete, WoActions.InputOutput, WoActions.HandoverFg],
        WoStatus.Completed => [WoActions.Resume, WoActions.Close, WoActions.HandoverFg],
        WoStatus.Closed => [WoActions.Reopen],
        _ => [],
    };

    private WoListItem ListItem(Wo wo)
    {
        var po = _pos.GetAsync(wo.PoId).Result;
        var fg = _fgs.GetAsync(wo.FgId).Result;
        var total = wo.Lines.Sum(l => l.Qty);
        var pct = total == 0 ? 0 : Math.Round(LastOutput(wo) * 100m / total, 1);
        return new WoListItem(wo.Id, wo.Code, wo.PoId, po.Code, po.CustomerName, fg.Code, fg.Name, wo.Status, total, pct, wo.Start, wo.Target,
            wo.Target is { } t && t < Today() && wo.Status is WoStatus.Released or WoStatus.InProgress);
    }

    private WoDto Dto(Wo wo)
    {
        var po = _pos.GetAsync(wo.PoId).Result;
        var fg = _fgs.GetAsync(wo.FgId).Result;
        var lines = wo.Lines.Select(l =>
        {
            var pl = po.Lines.First(x => x.Id == l.PoLineId);
            return new WoLineDto(l.PoLineId, l.PoLineId, pl.ColorSize, l.Qty, _handedOver.GetValueOrDefault((wo.Id, l.PoLineId)), pl.RemainingPlanQty);
        }).ToList();
        var total = lines.Sum(l => l.Qty);
        var handed = lines.Sum(l => l.HandedOverQty);
        var vendor = _vendor.Where(v => v.Key.Wo == wo.Id).Sum(v => Math.Max(0, v.Value.Sent - v.Value.Received));
        return new WoDto(wo.Id, wo.Code, po.Id, po.Code, po.CustomerPoNumber, po.CustomerName, fg.Id, fg.Code, fg.Name, wo.Status, wo.Start, wo.Target, wo.Notes, wo.CancelReason, lines, total,
            total == 0 ? 0 : Math.Round(LastOutput(wo) * 100m / total, 1), handed, Math.Max(0, LastOutput(wo) - handed), vendor, wo.ReleasedAt, Actions(wo), wo.Version);
    }

    // ----- Daftar -----

    public Task<PagedResult<WoListItem>> ListAsync(WoListQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<Wo> rows = _wos;
            if (!string.IsNullOrWhiteSpace(q.Q))
            {
                var s = q.Q.Trim();
                rows = rows.Where(w => { var i = ListItem(w); return i.Code.Contains(s, StringComparison.OrdinalIgnoreCase) || i.PoCode.Contains(s, StringComparison.OrdinalIgnoreCase) || i.CustomerName.Contains(s, StringComparison.OrdinalIgnoreCase); });
            }

            if (q.Status is { } st) { rows = rows.Where(w => w.Status == st); }
            if (q.PoId is { } po) { rows = rows.Where(w => w.PoId == po); }
            if (q.FgId is { } fg) { rows = rows.Where(w => w.FgId == fg); }
            if (q.From is { } f) { rows = rows.Where(w => w.Start >= f); }
            if (q.To is { } t) { rows = rows.Where(w => w.Start <= t); }
            var items = rows.Select(ListItem).ToList();
            if (q.Terlambat) { items = items.Where(i => i.IsLate).ToList(); }
            if (q.MenungguClose) { items = items.Where(i => i.Status == WoStatus.Completed).ToList(); }
            var all = items.OrderByDescending(i => i.Code).ToList();
            return Task.FromResult(new PagedResult<WoListItem>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), q.Page, q.PageSize, all.Count));
        }
    }

    public Task<WoCreationOptionsDto> GetCreationOptionsAsync(Guid poId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var po = _pos.GetAsync(poId).Result;
            if (po.Status is not (PoStatus.Confirmed or PoStatus.InProgress)) { throw Problem(ProblemTypes.StateConflict, 409, "PO belum dapat direncanakan.", $"PO berstatus {po.Status}."); }
            var fgs = po.Lines.Where(l => l.RemainingPlanQty > 0).GroupBy(l => l.FgId).Select(g =>
            {
                var readiness = _fgs.GetReadinessAsync(g.Key).Result;
                var fg = _fgs.GetAsync(g.Key).Result;
                return new WoCreationFgOption(fg.Id, fg.Code, fg.Name, readiness.IsReady, readiness.Items, g.Select(l => new WoCreationCell(l.Id, l.ColorSize, l.RemainingPlanQty)).ToList());
            }).ToList();
            return Task.FromResult(new WoCreationOptionsDto(po.Id, po.Code, po.CustomerName, fgs));
        }
    }

    public Task<WoDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(Dto(Find(id))); }
    }

    // ----- Simpan -----

    private void Validate(SaveWoRequest r, Wo? self)
    {
        var po = _pos.GetAsync(r.PoId).Result;
        if (po.Status is not (PoStatus.Confirmed or PoStatus.InProgress)) { throw Problem(ProblemTypes.StateConflict, 409, "PO belum dapat direncanakan.", $"PO berstatus {po.Status}."); }
        if (r.Lines is null || r.Lines.Count == 0 || r.Lines.All(l => l.Qty <= 0)) { throw Invalid("lines", "Minimal satu Qty terisi."); }
        if (r.StartDate is { } s && r.TargetDate is { } t && t < s) { throw Invalid("targetDate", "Target selesai tidak boleh sebelum tanggal mulai."); }
        var committed = _wos.Where(w => w != self && w.PoId == r.PoId && w.Status is WoStatus.Draft).ToList();
        for (var i = 0; i < r.Lines.Count; i++)
        {
            var l = r.Lines[i];
            var pl = po.Lines.FirstOrDefault(x => x.Id == l.PoLineId && x.FgId == r.FgId) ?? throw Invalid($"lines[{i}].poLineId", "Baris PO bukan milik FG ini.");
            if (l.Qty < 0) { throw Invalid($"lines[{i}].qty", "Qty tidak boleh negatif."); }
            if (l.Qty > pl.RemainingPlanQty)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Qty melebihi Sisa Rencana.", code: ProblemCodes.PoRemainingPlanExceeded,
                    violations: [new RuleViolation(ProblemCodes.PoRemainingPlanExceeded, $"{pl.ColorSize.ColorName} · {pl.ColorSize.SizeCode}", l.Qty, pl.RemainingPlanQty, $"Sisa Rencana tinggal {pl.RemainingPlanQty}.")]);
            }
        }

        _ = committed;
    }

    private string NextCode()
    {
        var prefix = $"WO-{Today():yyyyMM}-";
        var next = _wos.Where(w => w.Code.StartsWith(prefix, StringComparison.Ordinal)).Select(w => int.Parse(w.Code[prefix.Length..])).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }

    public Task<WoDto> CreateAsync(SaveWoRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            Validate(r, null);
            var wo = new Wo { Code = NextCode(), PoId = r.PoId, FgId = r.FgId, Start = r.StartDate, Target = r.TargetDate, Notes = Clean(r.Notes), Lines = r.Lines.Where(l => l.Qty > 0).Select(l => (l.PoLineId, l.Qty)).ToList() };
            wo.History.Add(new WoStatusHistoryItem(null, WoStatus.Draft, null, DateTimeOffset.UtcNow, "Administrator"));
            _wos.Add(wo);
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoDto> UpdateDraftAsync(Guid id, SaveWoRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Draft);
            Validate(r, wo);
            (wo.Start, wo.Target, wo.Notes) = (r.StartDate, r.TargetDate, Clean(r.Notes));
            wo.Lines = r.Lines.Where(l => l.Qty > 0).Select(l => (l.PoLineId, l.Qty)).ToList();
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoDto> UpdateQuantitiesAsync(Guid id, UpdateWoQuantitiesRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Released, WoStatus.InProgress);
            var po = _pos.GetAsync(wo.PoId).Result;
            if (r.Lines.Count != wo.Lines.Count || r.Lines.Any(l => wo.Lines.All(x => x.PoLineId != l.PoLineId))) { throw Invalid("lines", "Seluruh baris WO harus dikirim."); }
            var violations = new List<RuleViolation>();
            foreach (var l in r.Lines)
            {
                var old = wo.Lines.First(x => x.PoLineId == l.PoLineId).Qty;
                var pl = po.Lines.First(x => x.Id == l.PoLineId);
                if (l.Qty < FirstOutput(wo, l.PoLineId))
                {
                    violations.Add(new RuleViolation(ProblemCodes.WoQtyBelowOutput, $"{pl.ColorSize.ColorName} · {pl.ColorSize.SizeCode}", l.Qty, FirstOutput(wo, l.PoLineId), $"Qty minimal {FirstOutput(wo, l.PoLineId)} (output proses pertama)."));
                }
                else if (l.Qty - old > pl.RemainingPlanQty)
                {
                    violations.Add(new RuleViolation(ProblemCodes.PoRemainingPlanExceeded, $"{pl.ColorSize.ColorName} · {pl.ColorSize.SizeCode}", l.Qty, old + pl.RemainingPlanQty, $"Maksimal {old + pl.RemainingPlanQty} (Sisa Rencana PO)."));
                }
            }

            if (violations.Count > 0)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Qty WO tidak valid.", code: violations[0].Code, violations: violations);
            }

            foreach (var l in r.Lines)
            {
                var old = wo.Lines.First(x => x.PoLineId == l.PoLineId).Qty;
                if (l.Qty != old) { _pos.AdjustCommitted(l.PoLineId, deltaInProduction: l.Qty - old); }
            }

            wo.Lines = r.Lines.Select(l => (l.PoLineId, l.Qty)).ToList();
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoDto> UpdateScheduleAsync(Guid id, UpdateWoScheduleRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Draft, WoStatus.Released, WoStatus.InProgress);
            if (r.StartDate is { } s && r.TargetDate is { } t && t < s) { throw Invalid("targetDate", "Target selesai tidak boleh sebelum tanggal mulai."); }
            (wo.Start, wo.Target, wo.Notes) = (r.StartDate, r.TargetDate, Clean(r.Notes));
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    // ----- Rilis -----

    private WoReleasePreviewDto ReleaseChecks(Wo wo)
    {
        var po = _pos.GetAsync(wo.PoId).Result;
        var readiness = _fgs.GetReadinessAsync(wo.FgId).Result;
        var checks = new List<ReadinessItem>
        {
            new("po.active", po.Status is PoStatus.Confirmed or PoStatus.InProgress, po.Status is PoStatus.Confirmed or PoStatus.InProgress ? "PO aktif." : $"PO berstatus {po.Status}.", null),
            new("fg.ready", readiness.IsReady, readiness.IsReady ? "FG Siap Produksi." : "FG belum Siap Produksi.", $"/master/fgs/{wo.FgId}"),
        };
        checks.AddRange(readiness.Items.Where(i => !i.Ok).Select(i => new ReadinessItem("fg.detail", false, i.Message, $"/master/fgs/{wo.FgId}")));
        var overs = wo.Lines.Where(l => l.Qty > po.Lines.First(x => x.Id == l.PoLineId).RemainingPlanQty).ToList();
        checks.Add(new ReadinessItem("po.plan", overs.Count == 0, overs.Count == 0 ? "Qty ≤ Sisa Rencana PO." : $"{overs.Count} baris melebihi Sisa Rencana PO.", null));
        var routing = _fgs.GetRoutingAsync(wo.FgId).Result.Steps;
        var cells = _fgs.GetTariffsAsync(wo.FgId).Result.Cells.Count(c => c.Rate is not null);
        return new WoReleasePreviewDto(checks, checks.All(c => c.Ok), routing.Count, cells);
    }

    public Task<WoReleasePreviewDto> PreviewReleaseAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { var wo = Find(id); RequireState(wo, WoStatus.Draft); return Task.FromResult(ReleaseChecks(wo)); }
    }

    public Task<WoDto> ReleaseAsync(Guid id, WoActionRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Draft);
            var preview = ReleaseChecks(wo);
            if (!preview.CanRelease)
            {
                var failed = preview.Checks.Where(c => !c.Ok).Select(c => new RuleViolation(c.Code == "po.plan" ? ProblemCodes.PoRemainingPlanExceeded : ProblemCodes.FgNotReady, c.Code, null, null, c.Message)).ToList();
                throw Problem(ProblemTypes.RuleViolation, 409, "WO belum dapat dirilis.", code: failed[0].Code, violations: failed);
            }

            var grid = _fgs.GetTariffsAsync(wo.FgId).Result;
            wo.Routing = [.. _fgs.GetRoutingAsync(wo.FgId).Result.Steps];
            wo.TariffSizes = [.. grid.Sizes];
            wo.Tariffs = grid.Cells.Where(c => c.Rate is not null).ToDictionary(c => (c.ProcessId, c.SizeId), c => c.Rate!.Value);
            foreach (var l in wo.Lines) { _pos.AdjustCommitted(l.PoLineId, deltaInProduction: l.Qty); }
            wo.ReleasedAt = DateTimeOffset.UtcNow;
            Move(wo, WoStatus.Released, Clean(r.Reason));
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoDto> CompleteAsync(Guid id, WoActionRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.InProgress);
            Move(wo, WoStatus.Completed, Clean(r.Reason));
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoDto> ResumeAsync(Guid id, WoActionRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Completed);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            Move(wo, WoStatus.InProgress, r.Reason.Trim());
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoClosePreviewDto> PreviewCloseAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            RequireState(wo, WoStatus.Completed);
            var po = _pos.GetAsync(wo.PoId).Result;
            var unhanded = wo.Lines.Select(l => (Line: l, Pending: _output.GetValueOrDefault((wo.Id, wo.Routing[^1].Seq, l.PoLineId)) - _handedOver.GetValueOrDefault((wo.Id, l.PoLineId))))
                .Where(x => x.Pending > 0).Select(x => new WoUnhandedCell(po.Lines.First(p => p.Id == x.Line.PoLineId).ColorSize, x.Pending)).ToList();
            var returned = wo.Lines.Sum(l => Math.Max(0, l.Qty - _output.GetValueOrDefault((wo.Id, wo.Routing[^1].Seq, l.PoLineId))));
            return Task.FromResult(new WoClosePreviewDto(wo.Lines.Sum(l => l.Qty), returned, unhanded));
        }
    }

    public Task<WoDto> CloseAsync(Guid id, WoActionRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Completed);
            foreach (var l in wo.Lines) { _pos.AdjustCommitted(l.PoLineId, deltaInProduction: -l.Qty); }
            Move(wo, WoStatus.Closed, Clean(r.Reason));
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoDto> ReopenAsync(Guid id, WoActionRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Closed);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            var po = _pos.GetAsync(wo.PoId).Result;
            var over = wo.Lines.Where(l => l.Qty > po.Lines.First(x => x.Id == l.PoLineId).RemainingPlanQty).ToList();
            if (over.Count > 0)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Sisa Rencana PO tidak cukup untuk membuka kembali WO.", code: ProblemCodes.PoRemainingPlanExceeded,
                    violations: over.Select(l => new RuleViolation(ProblemCodes.PoRemainingPlanExceeded, "Baris PO", l.Qty, po.Lines.First(x => x.Id == l.PoLineId).RemainingPlanQty, "Sisa Rencana tidak cukup.")).ToList());
            }

            foreach (var l in wo.Lines) { _pos.AdjustCommitted(l.PoLineId, deltaInProduction: l.Qty); }
            Move(wo, WoStatus.Completed, r.Reason.Trim());
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    public Task<WoDto> CancelAsync(Guid id, WoActionRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            CheckVersion(wo, r.RowVersion);
            RequireState(wo, WoStatus.Draft, WoStatus.Released);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            if (HasActivity(wo)) { throw Problem(ProblemTypes.RuleViolation, 409, "WO sudah memiliki aktivitas.", "Ada output, pengiriman vendor, atau serah terima.", ProblemCodes.WoHasActivity); }
            if (wo.Status == WoStatus.Released) { foreach (var l in wo.Lines) { _pos.AdjustCommitted(l.PoLineId, deltaInProduction: -l.Qty); } }
            wo.CancelReason = r.Reason.Trim();
            Move(wo, WoStatus.Cancelled, wo.CancelReason);
            wo.Version++;
            return Task.FromResult(Dto(wo));
        }
    }

    // ----- Progres, snapshot, riwayat -----

    public Task<WoProgressDto> GetProgressAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            var dto = Dto(wo);
            var routing = wo.Routing.Count > 0 ? wo.Routing : [.. _fgs.GetRoutingAsync(wo.FgId).Result.Steps];
            var steps = routing.Select(s =>
            {
                var cells = wo.Lines.Select(l => new WoProgressCell(l.PoLineId, _output.GetValueOrDefault((wo.Id, s.Seq, l.PoLineId)), l.Qty, 0)).ToList();
                var v = _vendor.GetValueOrDefault((wo.Id, s.Seq));
                var output = s.ProcessType == ProcessType.Vendor ? v.Received : cells.Sum(c => c.Output);
                var target = cells.Sum(c => c.Target);
                var state = output == 0 && v.Sent == 0 ? StepState.NotStarted : output >= target && target > 0 ? StepState.Done : StepState.InProgress;
                return new WoStepProgress(s.Seq, s.ProcessCode, s.ProcessName, s.ProcessType, cells, output, target, target == 0 ? 0 : Math.Round(output * 100m / target, 1), state, v.Sent, Math.Max(0, v.Sent - v.Received));
            }).ToList();
            return Task.FromResult(new WoProgressDto(wo.Id, dto.Lines, steps, dto.HandedOverQty, dto.UnhandedQty));
        }
    }

    public Task<WoTariffSnapshotDto> GetTariffSnapshotAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Find(id);
            if (wo.ReleasedAt is null) { throw Problem(ProblemTypes.StateConflict, 409, "Snapshot tarif tersedia setelah WO dirilis."); }
            var internalSteps = wo.Routing.Where(s => s.ProcessType == ProcessType.Internal).ToList();
            return Task.FromResult(new WoTariffSnapshotDto(wo.ReleasedAt, "Administrator", wo.Routing.Select(s => new WoSnapshotStep(s.Seq, s.ProcessCode, s.ProcessName, s.ProcessType)).ToList(), wo.TariffSizes,
                internalSteps.SelectMany(s => wo.TariffSizes.Where(z => wo.Tariffs.ContainsKey((s.ProcessId, z.Id))).Select(z => new WoSnapshotRate(s.Seq, z.Id, wo.Tariffs[(s.ProcessId, z.Id)]))).ToList()));
        }
    }

    public Task<IReadOnlyList<WoStatusHistoryItem>> GetStatusHistoryAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<WoStatusHistoryItem>>(Find(id).History.OrderByDescending(h => h.ChangedAt).ToList()); }
    }
}
