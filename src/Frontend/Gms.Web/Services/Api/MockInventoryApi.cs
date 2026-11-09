using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Inventory;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.WorkOrders;

namespace Gms.Web.Services.Api;

/// <summary>
/// Serah Terima FG, Serah Terima Material, dan Stok FG tiruan (ADR-014): batas serah = output langkah terakhir − sudah diserahkan, alokasi otomatis ke baris PO
/// (WO Qty), kelebihan menjadi stok bebas, void/koreksi dengan pratinjau dampak stok, kartu stok dengan saldo berjalan.
/// </summary>
public sealed class MockInventoryApi : IInventoryApi
{
    private sealed class Handover
    {
        public Guid Id = Guid.NewGuid(); public DateOnly Date; public string? DocNo, ReceivedBy, Notes, VoidReason; public Guid WoId; public Dictionary<Guid, (int Qty, int Auto)> Lines = [];
        public DocStatus Status; public Guid? ReplacesId, ReplacedById; public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
    }

    private sealed record Ledger(long Id, Guid Fg, Guid Color, Guid Size, DateOnly Date, StockLedgerKind Kind, string? SourceType, Guid? SourceId, string? Label, int Qty, string? Reason);

    private sealed class MaterialDoc
    {
        public Guid Id = Guid.NewGuid(); public DateOnly Date; public string? DocNo, Notes, VoidReason; public Guid WoId; public List<MaterialHandoverLine> Lines = []; public DocStatus Status;
    }

    private readonly object _gate = new();
    private readonly List<Handover> _handovers = [];
    private readonly List<Ledger> _ledger = [];
    private readonly List<MaterialDoc> _materials = [];
    private long _ledgerId = 1;
    private readonly MockPoApi _pos;
    private readonly MockWoApi _wos;
    private readonly MockProductionApi _production;
    private readonly MockFgApi _fgs;
    private readonly MockColorApi _colors;
    private readonly MockSizeApi _sizes;
    private readonly MockMaterialApi _materialMaster;
    private int _docSeq = 1;

    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public MockInventoryApi(MockPoApi pos, MockWoApi wos, MockProductionApi production, MockFgApi fgs, MockColorApi colors, MockSizeApi sizes, MockMaterialApi materialMaster)
    {
        _pos = pos;
        _wos = wos;
        _production = production;
        _fgs = fgs;
        _colors = colors;
        _sizes = sizes;
        _materialMaster = materialMaster;
        foreach (var s in pos.StockSnapshot().Where(s => s.OnHand > 0))
        {
            _ledger.Add(new Ledger(_ledgerId++, s.Fg, s.Color, s.Size, Today().AddDays(-9), StockLedgerKind.OpeningBalance, null, null, "Saldo Awal", s.OnHand, null));
        }
    }

    private static ApiException Problem(string type, int status, string title, string? detail = null, string? code = null, IReadOnlyList<RuleViolation>? violations = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, Code = code, Violations = violations, Errors = errors, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    private static ApiException Invalid(string path, string message) =>
        Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: new Dictionary<string, string[]> { [path] = [message] });

    private WoDto Wo(Guid id) => _wos.GetAsync(id).GetAwaiter().GetResult();

    private static string Label(ColorSizeRef c) => $"{c.ColorName} · {c.SizeCode}";

    private void AddLedger(Guid fg, Guid color, Guid size, DateOnly date, StockLedgerKind kind, Guid? source, string? label, int qty, string? reason = null) =>
        _ledger.Add(new Ledger(_ledgerId++, fg, color, size, date, kind, source is null ? null : "Handover", source, label, qty, reason));

    private (int OnHand, int Allocated) Stock(Guid fg, Guid color, Guid size)
    {
        var s = _pos.StockSnapshot().FirstOrDefault(x => x.Fg == fg && x.Color == color && x.Size == size);
        return (s.OnHand, s.Allocated);
    }

    // ----- Serah Terima FG -----

    public Task<HandoverFgContextDto> GetHandoverFgContextAsync(Guid woId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Wo(woId);
            var blocked = wo.Status is WoStatus.InProgress or WoStatus.Completed ? null : $"WO ini berstatus {wo.Status}. Serah terima hanya untuk WO In Progress atau Completed.";
            var cols = wo.Lines.Select(l =>
            {
                var output = _production.LastStepOutput(woId, l.PoLineId);
                return new HandoverColumn(l.PoLineId, l.ColorSize, Math.Max(0, output - l.HandedOverQty), l.HandedOverQty, output, l.Qty);
            }).ToList();
            return Task.FromResult(new HandoverFgContextDto(woId, wo.Code, wo.PoCode, wo.FgCode, wo.FgName, cols, blocked));
        }
    }

    private (Dictionary<Guid, (int Qty, int Auto)> Lines, int Auto, int Free) Split(WoDto wo, IReadOnlyList<WoLineQty> cells, Handover? replacing = null)
    {
        var lines = new Dictionary<Guid, (int, int)>();
        int auto = 0, free = 0;
        foreach (var c in cells.Where(c => c.Qty > 0))
        {
            var line = wo.Lines.FirstOrDefault(l => l.PoLineId == c.WoLineId) ?? throw Invalid("cells", "Baris WO tidak ditemukan.");
            var before = line.HandedOverQty - (replacing?.Lines.GetValueOrDefault(line.PoLineId).Qty ?? 0);
            var a = Math.Max(0, Math.Min(c.Qty, line.Qty - before));
            lines[c.WoLineId] = (c.Qty, a);
            auto += a;
            free += c.Qty - a;
        }

        return (lines, auto, free);
    }

    private void CheckHandover(WoDto wo, IReadOnlyList<WoLineQty> cells, DateOnly date, Handover? replacing = null)
    {
        if (wo.Status is not (WoStatus.InProgress or WoStatus.Completed)) { throw Problem(ProblemTypes.StateConflict, 409, "Status WO tidak mengizinkan serah terima.", $"WO berstatus {wo.Status}.", ProblemCodes.WoStatusInvalid); }
        if (date > Today()) { throw Invalid("date", "Tanggal tidak boleh di masa depan."); }
        if (cells.All(c => c.Qty <= 0)) { throw Invalid("cells", "Isi minimal satu Qty."); }
        var violations = new List<RuleViolation>();
        foreach (var c in cells.Where(c => c.Qty > 0))
        {
            var line = wo.Lines.FirstOrDefault(l => l.PoLineId == c.WoLineId) ?? throw Invalid("cells", "Baris WO tidak ditemukan.");
            var max = _production.LastStepOutput(wo.Id, line.PoLineId) - (line.HandedOverQty - (replacing?.Lines.GetValueOrDefault(line.PoLineId).Qty ?? 0));
            if (c.Qty > max) { violations.Add(new RuleViolation(ProblemCodes.HandoverExceedsOutput, Label(line.ColorSize), c.Qty, max, $"Maksimal {Math.Max(0, max)} (output langkah terakhir − sudah diserahterimakan).")); }
        }

        if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Qty melebihi output langkah terakhir.", code: ProblemCodes.HandoverExceedsOutput, violations: violations); }
    }

    public Task<HandoverFgPreviewDto> PreviewHandoverFgAsync(SaveHandoverFgRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Wo(r.WoId);
            CheckHandover(wo, r.Cells, r.Date);
            var (lines, auto, free) = Split(wo, r.Cells);
            return Task.FromResult(new HandoverFgPreviewDto(auto, free, wo.PoCode, lines.Select(l => new HandoverCellEstimate(l.Key, l.Value.Auto, l.Value.Qty - l.Value.Auto)).ToList()));
        }
    }

    private void Apply(Handover h, int sign)
    {
        var wo = Wo(h.WoId);
        foreach (var (lineId, (qty, auto)) in h.Lines)
        {
            var line = wo.Lines.First(l => l.PoLineId == lineId);
            _wos.AddHandedOver(h.WoId, lineId, sign * qty);
            _pos.AdjustStock(wo.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId, sign * qty);
            if (auto > 0) { _pos.AdjustCommitted(lineId, deltaInProduction: -sign * auto, deltaAllocated: sign * auto); }
            AddLedger(wo.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId, h.Date, sign > 0 ? StockLedgerKind.HandoverReceipt : StockLedgerKind.HandoverReversal, h.Id, h.DocNo ?? wo.Code, sign * qty);
        }
    }

    public Task<HandoverFgResultDto> CreateHandoverFgAsync(SaveHandoverFgRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Wo(r.WoId);
            CheckHandover(wo, r.Cells, r.Date);
            var (lines, auto, free) = Split(wo, r.Cells);
            var h = new Handover { Date = r.Date, DocNo = string.IsNullOrWhiteSpace(r.DocNo) ? $"ST-{_docSeq++:0000}" : r.DocNo.Trim(), ReceivedBy = Clean(r.ReceivedBy), Notes = Clean(r.Notes), WoId = r.WoId, Lines = lines, Status = DocStatus.Active };
            _handovers.Add(h);
            Apply(h, 1);
            return Task.FromResult(new HandoverFgResultDto(h.Id, auto, free, wo.PoCode));
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private HandoverFgListItem Item(Handover h)
    {
        var wo = Wo(h.WoId);
        return new HandoverFgListItem(h.Id, h.Date, h.DocNo, h.WoId, wo.Code, wo.PoCode, wo.FgCode, h.Lines.Values.Sum(l => l.Qty), h.Status, "Administrator");
    }

    public Task<PagedResult<HandoverFgListItem>> ListHandoverFgAsync(HandoverFgListQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<Handover> rows = _handovers;
            if (q.WoId is { } w) { rows = rows.Where(h => h.WoId == w); }
            if (q.From is { } f) { rows = rows.Where(h => h.Date >= f); }
            if (q.To is { } t) { rows = rows.Where(h => h.Date <= t); }
            if (q.Status is { } s) { rows = rows.Where(h => h.Status == s); }
            if (q.FgId is { } fg) { rows = rows.Where(h => Wo(h.WoId).FgId == fg); }
            if (q.PoId is { } po) { rows = rows.Where(h => Wo(h.WoId).PoId == po); }
            var all = rows.OrderByDescending(h => h.CreatedAt).ToList();
            return Task.FromResult(new PagedResult<HandoverFgListItem>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(Item).ToList(), q.Page, q.PageSize, all.Count));
        }
    }

    private Handover Find(Guid id) => _handovers.FirstOrDefault(h => h.Id == id) ?? throw Problem(ProblemTypes.NotFound, 404, "Serah terima tidak ditemukan.");

    public Task<HandoverFgDto> GetHandoverFgAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var h = Find(id);
            var wo = Wo(h.WoId);
            var active = h.Status == DocStatus.Active;
            var allowed = active && wo.Status is WoStatus.InProgress or WoStatus.Completed;
            return Task.FromResult(new HandoverFgDto(Item(h), h.Lines.Select(l => new HandoverFgLineDto(l.Key, wo.Lines.First(x => x.PoLineId == l.Key).ColorSize, l.Value.Qty, l.Value.Auto)).ToList(),
                h.ReceivedBy, h.Notes, h.VoidReason, h.ReplacesId, h.ReplacedById, allowed, allowed, active && !allowed ? $"WO berstatus {wo.Status}." : null));
        }
    }

    /// <summary>Dampak stok bila entri <paramref name="h"/> dihapus lalu (opsional) diganti dengan <paramref name="replacement"/>.</summary>
    private (List<ImpactRow> Rows, List<RuleViolation> Violations, int Released) StockImpact(Handover h, IReadOnlyDictionary<Guid, int>? replacement)
    {
        var wo = Wo(h.WoId);
        var rows = new List<ImpactRow>();
        var violations = new List<RuleViolation>();
        var released = 0;
        foreach (var line in wo.Lines.Where(l => h.Lines.ContainsKey(l.PoLineId) || (replacement?.ContainsKey(l.PoLineId) ?? false)))
        {
            var old = h.Lines.GetValueOrDefault(line.PoLineId);
            var oldAuto = h.Lines.GetValueOrDefault(line.PoLineId).Auto;
            var neu = replacement?.GetValueOrDefault(line.PoLineId) ?? 0;
            var (onHand, allocated) = Stock(wo.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId);
            var newOnHand = onHand - old.Qty + neu;
            var newAllocated = allocated - oldAuto + (replacement is null ? 0 : Math.Max(0, Math.Min(neu, line.Qty - (line.HandedOverQty - old.Qty))));
            var ok = newOnHand >= 0 && newOnHand - newAllocated >= 0;
            var message = ok ? null : newOnHand < 0 ? "Stok fisik tidak cukup (sudah dikirim)." : "Stok sudah dialokasikan ke PO lain atau sudah dikirim.";
            released += oldAuto;
            rows.Add(new ImpactRow($"{wo.FgCode} · {Label(line.ColorSize)}", "Fisik", onHand, newOnHand, newAllocated, ok, message));
            if (!ok) { violations.Add(new RuleViolation("stock.insufficient", $"{wo.FgCode} · {Label(line.ColorSize)}", newOnHand, newAllocated, message!)); }
        }

        return (rows, violations, released);
    }

    public Task<ImpactPreview> PreviewVoidHandoverFgAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var (rows, violations, released) = StockImpact(Find(id), null);
            return Task.FromResult(new ImpactPreview(rows, violations.Count == 0, violations, [$"Alokasi otomatis yang akan dilepas: {released} pcs"]));
        }
    }

    public Task VoidHandoverFgAsync(Guid id, ReasonRequest request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var h = Find(id);
            if (string.IsNullOrWhiteSpace(request.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            if (h.Status == DocStatus.Voided) { throw Problem(ProblemTypes.StateConflict, 409, "Serah terima sudah di-void."); }
            var (_, violations, _) = StockImpact(h, null);
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Void ditolak: stok tidak cukup.", code: "stock.insufficient", violations: violations); }
            Apply(h, -1);
            h.Status = DocStatus.Voided;
            h.VoidReason = request.Reason.Trim();
            return Task.CompletedTask;
        }
    }

    public Task<ImpactPreview> PreviewCorrectHandoverFgAsync(Guid id, CorrectHandoverFgRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var h = Find(id);
            CheckHandover(Wo(h.WoId), r.Cells, r.Date, h);
            var (rows, violations, _) = StockImpact(h, r.Cells.Where(c => c.Qty > 0).ToDictionary(c => c.WoLineId, c => c.Qty));
            return Task.FromResult(new ImpactPreview(rows, violations.Count == 0, violations, []));
        }
    }

    public Task<HandoverFgResultDto> CorrectHandoverFgAsync(Guid id, CorrectHandoverFgRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var h = Find(id);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            var wo = Wo(h.WoId);
            CheckHandover(wo, r.Cells, r.Date, h);
            var (_, violations, _) = StockImpact(h, r.Cells.Where(c => c.Qty > 0).ToDictionary(c => c.WoLineId, c => c.Qty));
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Koreksi ditolak: stok tidak cukup.", code: "stock.insufficient", violations: violations); }
            Apply(h, -1);
            h.Status = DocStatus.Voided;
            h.VoidReason = r.Reason.Trim();
            var (lines, auto, free) = Split(Wo(h.WoId), r.Cells);
            var n = new Handover { Date = r.Date, DocNo = Clean(r.DocNo) ?? h.DocNo, ReceivedBy = Clean(r.ReceivedBy), Notes = Clean(r.Notes), WoId = h.WoId, Lines = lines, Status = DocStatus.Active, ReplacesId = h.Id };
            h.ReplacedById = n.Id;
            _handovers.Add(n);
            Apply(n, 1);
            return Task.FromResult(new HandoverFgResultDto(n.Id, auto, free, wo.PoCode));
        }
    }

    // ----- Material -----

    public Task<Guid> CreateMaterialHandoverAsync(SaveMaterialHandoverRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var wo = Wo(r.WoId);
            if (wo.Status == WoStatus.Cancelled) { throw Problem(ProblemTypes.StateConflict, 409, "WO dibatalkan.", code: ProblemCodes.WoStatusInvalid); }
            if (r.Date > Today()) { throw Invalid("date", "Tanggal tidak boleh di masa depan."); }
            if (r.Lines is null || r.Lines.Count == 0) { throw Invalid("lines", "Minimal satu material."); }
            for (var i = 0; i < r.Lines.Count; i++)
            {
                if (!_materialMaster.Contains(r.Lines[i].MaterialId)) { throw Invalid($"lines[{i}].materialId", "Material tidak ditemukan."); }
                if (r.Lines[i].Qty <= 0) { throw Invalid($"lines[{i}].qty", "Qty harus lebih dari 0."); }
                if (decimal.Round(r.Lines[i].Qty, 3) != r.Lines[i].Qty) { throw Invalid($"lines[{i}].qty", "Qty maksimal 3 digit di belakang koma."); }
            }

            var doc = new MaterialDoc { Date = r.Date, DocNo = Clean(r.DocNo), Notes = Clean(r.Notes), WoId = r.WoId, Lines = [.. r.Lines], Status = DocStatus.Active };
            _materials.Add(doc);
            return Task.FromResult(doc.Id);
        }
    }

    private MaterialHandoverListItem MaterialItem(MaterialDoc d) => new(d.Id, d.Date, d.DocNo, d.WoId, Wo(d.WoId).Code, d.Lines.Count, d.Status, "Administrator");

    public Task<PagedResult<MaterialHandoverListItem>> ListMaterialHandoversAsync(MaterialHandoverListQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<MaterialDoc> rows = _materials;
            if (q.WoId is { } w) { rows = rows.Where(d => d.WoId == w); }
            if (q.Status is { } s) { rows = rows.Where(d => d.Status == s); }
            if (q.From is { } f) { rows = rows.Where(d => d.Date >= f); }
            if (q.To is { } t) { rows = rows.Where(d => d.Date <= t); }
            var all = rows.OrderByDescending(d => d.Date).ToList();
            return Task.FromResult(new PagedResult<MaterialHandoverListItem>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(MaterialItem).ToList(), q.Page, q.PageSize, all.Count));
        }
    }

    private MaterialDoc FindMaterial(Guid id) => _materials.FirstOrDefault(d => d.Id == id) ?? throw Problem(ProblemTypes.NotFound, 404, "Dokumen tidak ditemukan.");

    public Task<MaterialHandoverDto> GetMaterialHandoverAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = FindMaterial(id);
            var lines = d.Lines.Select(l => { var m = _materialMaster.GetAsync(l.MaterialId).GetAwaiter().GetResult(); return new MaterialHandoverLineDto(m.Id, m.Code, m.Name, m.UomCode, l.Qty, l.Notes); }).ToList();
            return Task.FromResult(new MaterialHandoverDto(MaterialItem(d), lines, d.Notes, d.VoidReason, d.Status == DocStatus.Active));
        }
    }

    public Task VoidMaterialHandoverAsync(Guid id, ReasonRequest request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = FindMaterial(id);
            if (string.IsNullOrWhiteSpace(request.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            if (d.Status == DocStatus.Voided) { throw Problem(ProblemTypes.StateConflict, 409, "Dokumen sudah di-void."); }
            d.Status = DocStatus.Voided;
            d.VoidReason = request.Reason.Trim();
            return Task.CompletedTask;
        }
    }

    // ----- Stok -----

    private StockRow Row(Guid fg, Guid color, Guid size, int onHand, int allocated)
    {
        var f = _fgs.GetAsync(fg).GetAwaiter().GetResult();
        var c = _colors.GetAsync(color).GetAwaiter().GetResult();
        var s = _sizes.GetAsync(size).GetAwaiter().GetResult();
        return new StockRow(fg, f.Code, f.Name, new ColorSizeRef(c.Id, c.Code, c.Name, s.Id, s.Code, s.Name, s.SortOrder), onHand, allocated, Math.Max(0, onHand - allocated));
    }

    public Task<StockListResult> ListStockAsync(StockQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var rows = _pos.StockSnapshot().Select(s => Row(s.Fg, s.Color, s.Size, s.OnHand, s.Allocated)).ToList();
            IEnumerable<StockRow> query = rows;
            if (q.FgId is { } fg) { query = query.Where(r => r.FgId == fg); }
            if (q.ColorId is { } c) { query = query.Where(r => r.ColorSize.ColorId == c); }
            if (q.SizeId is { } s) { query = query.Where(r => r.ColorSize.SizeId == s); }
            if (q.HanyaAdaStok) { query = query.Where(r => r.OnHand > 0); }
            if (!string.IsNullOrWhiteSpace(q.Q)) { query = query.Where(r => r.FgCode.Contains(q.Q.Trim(), StringComparison.OrdinalIgnoreCase) || r.FgName.Contains(q.Q.Trim(), StringComparison.OrdinalIgnoreCase)); }
            var all = query.OrderBy(r => r.FgCode).ThenBy(r => r.ColorSize.ColorName).ThenBy(r => r.ColorSize.SizeSortOrder).ToList();
            return Task.FromResult(new StockListResult(new PagedResult<StockRow>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), q.Page, q.PageSize, all.Count),
                all.Sum(r => r.OnHand), all.Sum(r => r.Allocated), all.Sum(r => r.Available)));
        }
    }

    public Task<StockLedgerResult> GetStockLedgerAsync(StockLedgerQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var entries = _ledger.Where(l => l.Fg == q.FgId && l.Color == q.ColorId && l.Size == q.SizeId).OrderBy(l => l.Id).ToList();
            var running = 0;
            var rows = entries.Select(l => new StockLedgerRow(l.Id, l.Date, l.Kind, l.SourceType, l.SourceId, l.Label, l.Qty, running += l.Qty, l.Reason, "Administrator")).ToList();
            IEnumerable<StockLedgerRow> filtered = rows;
            if (q.From is { } f) { filtered = filtered.Where(r => r.EntryDate >= f); }
            if (q.To is { } t) { filtered = filtered.Where(r => r.EntryDate <= t); }
            var all = filtered.OrderByDescending(r => r.Id).ToList();
            var (onHand, allocated) = Stock(q.FgId, q.ColorId, q.SizeId);
            return Task.FromResult(new StockLedgerResult(Row(q.FgId, q.ColorId, q.SizeId, onHand, allocated),
                new PagedResult<StockLedgerRow>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), q.Page, q.PageSize, all.Count)));
        }
    }

    private (ImpactPreview Preview, int OnHand) AdjustImpact(AdjustStockRequest r)
    {
        if (r.Qty == 0) { throw Invalid("qty", "Qty penyesuaian tidak boleh 0."); }
        if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
        if (r.Date > Today()) { throw Invalid("date", "Tanggal tidak boleh di masa depan."); }
        if (!_fgs.GetSizesAsync(r.FgId).GetAwaiter().GetResult().SizeIds.Contains(r.SizeId)) { throw Invalid("sizeId", "Size bukan Size Berlaku FG ini."); }
        var (onHand, allocated) = Stock(r.FgId, r.ColorId, r.SizeId);
        var after = onHand + r.Qty;
        var ok = after >= 0 && after - allocated >= 0;
        var message = ok ? null : after < 0 ? "Stok fisik tidak boleh negatif." : "Tersedia menjadi negatif. Lepas alokasi terlebih dahulu.";
        var subject = $"{_fgs.GetAsync(r.FgId).GetAwaiter().GetResult().Code} · {_colors.GetAsync(r.ColorId).GetAwaiter().GetResult().Name} · {_sizes.GetAsync(r.SizeId).GetAwaiter().GetResult().Code}";
        var violations = ok ? [] : new List<RuleViolation> { new("stock.insufficient", subject, after, allocated, message!) };
        return (new ImpactPreview([new ImpactRow(subject, "Fisik", onHand, after, allocated, ok, message)], ok, violations, []), onHand);
    }

    public Task<ImpactPreview> PreviewAdjustStockAsync(AdjustStockRequest r, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(AdjustImpact(r).Preview); }
    }

    public Task AdjustStockAsync(AdjustStockRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var (preview, _) = AdjustImpact(r);
            if (!preview.CanProceed) { throw Problem(ProblemTypes.RuleViolation, 409, "Penyesuaian ditolak.", code: "stock.insufficient", violations: preview.Violations); }
            _pos.AdjustStock(r.FgId, r.ColorId, r.SizeId, r.Qty);
            AddLedger(r.FgId, r.ColorId, r.SizeId, r.Date, StockLedgerKind.Adjustment, null, "Penyesuaian", r.Qty, r.Reason.Trim());
            return Task.CompletedTask;
        }
    }

    /// <summary>Saldo awal (dipakai impor CSV tiruan): menambah stok fisik dan mencatat kartu stok.</summary>
    public void AddOpeningBalance(Guid fg, Guid color, Guid size, int qty, DateOnly date)
    {
        lock (_gate)
        {
            _pos.AdjustStock(fg, color, size, qty);
            AddLedger(fg, color, size, date, StockLedgerKind.OpeningBalance, null, "Saldo Awal", qty);
        }
    }
}
