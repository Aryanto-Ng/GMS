using System.Text;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Deliveries;
using Gms.Shared.Enums;
using Gms.Shared.Problems;

namespace Gms.Web.Services.Api;

/// <summary>
/// Pengiriman tiruan (ADR-014): Maks kirim = min(Dialokasikan, Qty − Terkirim); kirim mengurangi stok fisik + alokasi dan menambah Terkirim; void/koreksi mengembalikan;
/// PO Completed saat semua baris terkirim; surat jalan PDF tiruan (print_count naik).
/// </summary>
public sealed class MockDeliveriesApi : IDeliveriesApi
{
    private sealed class Delivery
    {
        public Guid Id = Guid.NewGuid(); public string Code = ""; public Guid PoId; public DateOnly Date; public string? Recipient, Vehicle, Driver, Notes, VoidReason;
        public Dictionary<Guid, int> Lines = []; public DocStatus Status; public int Prints; public Guid? ReplacesId, ReplacedById; public uint Version = 1;
    }

    private readonly object _gate = new();
    private readonly List<Delivery> _deliveries = [];
    private readonly MockPoApi _pos;
    private readonly MockInventoryApi _inventory;
    private readonly MockCustomerApi _customers;

    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public MockDeliveriesApi(MockPoApi pos, MockInventoryApi inventory, MockCustomerApi customers)
    {
        _pos = pos;
        _inventory = inventory;
        _customers = customers;
    }

    private static ApiException Problem(string type, int status, string title, string? detail = null, string? code = null, IReadOnlyList<RuleViolation>? violations = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, Code = code, Violations = violations, Errors = errors, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    private static ApiException Invalid(string path, string message) =>
        Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: new Dictionary<string, string[]> { [path] = [message] });

    private PoDto Po(Guid id) => _pos.GetAsync(id).GetAwaiter().GetResult();

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Label(ColorSizeRef c) => $"{c.ColorName} · {c.SizeCode}";

    private int StockAvailable(PoLineDto l)
    {
        var s = _pos.StockSnapshot().FirstOrDefault(x => x.Fg == l.FgId && x.Color == l.ColorSize.ColorId && x.Size == l.ColorSize.SizeId);
        return Math.Max(0, s.OnHand - s.Allocated);
    }

    private static int MaxDeliver(PoLineDto l) => Math.Max(0, Math.Min(l.AllocatedQty, l.Qty - l.DeliveredQty));

    public Task<IReadOnlyList<EligiblePoItem>> ListEligiblePosAsync(string? q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var items = new List<EligiblePoItem>();
            foreach (var status in new[] { PoStatus.Confirmed, PoStatus.InProgress })
            {
                foreach (var p in _pos.ListAsync(new PoListQuery { Status = status, PageSize = 100 }).GetAwaiter().GetResult().Items)
                {
                    var dto = Po(p.Id);
                    var open = dto.Lines.Sum(l => l.Qty - l.DeliveredQty);
                    if (open > 0 && (string.IsNullOrWhiteSpace(q) || p.Code.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase) || p.CustomerName.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(new EligiblePoItem(p.Id, p.Code, p.CustomerPoNumber, p.CustomerName, open, dto.AllocatedQty));
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<EligiblePoItem>>(items);
        }
    }

    public Task<DeliveryContextDto> GetContextAsync(Guid poId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var po = Po(poId);
            if (po.Status is not (PoStatus.Confirmed or PoStatus.InProgress)) { throw Problem(ProblemTypes.StateConflict, 409, "PO tidak dapat dikirim.", $"PO berstatus {po.Status}."); }
            var lines = po.Lines.Where(l => l.Qty - l.DeliveredQty > 0)
                .Select(l => new DeliveryLineOption(l.Id, l.FgCode, l.FgName, l.ColorSize, l.DeliveryDate, l.Qty, l.DeliveredQty, l.AllocatedQty, MaxDeliver(l), StockAvailable(l), l.IsLate)).ToList();
            return Task.FromResult(new DeliveryContextDto(po.Id, po.Code, po.CustomerPoNumber, po.CustomerName, po.Status, lines, []));
        }
    }

    private string NextCode(DateOnly date)
    {
        var prefix = $"SJ-{date:yyyyMM}-";
        var next = _deliveries.Where(d => d.Code.StartsWith(prefix, StringComparison.Ordinal)).Select(d => int.Parse(d.Code[prefix.Length..])).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }

    /// <summary>Memeriksa batas kirim pada kondisi akhir (dokumen <paramref name="replacing"/> dianggap sudah dikembalikan).</summary>
    private List<RuleViolation> Check(PoDto po, IEnumerable<DeliveryLineQty> lines, Delivery? replacing)
    {
        var violations = new List<RuleViolation>();
        foreach (var l in lines.Where(x => x.Qty > 0))
        {
            var line = po.Lines.FirstOrDefault(x => x.Id == l.PoLineId) ?? throw Invalid("lines", "Baris PO tidak ditemukan.");
            var back = replacing?.Lines.GetValueOrDefault(line.Id) ?? 0;
            var max = Math.Max(0, Math.Min(line.AllocatedQty + back, line.Qty - (line.DeliveredQty - back)));
            if (l.Qty > max) { violations.Add(new RuleViolation(ProblemCodes.DeliveryExceedsAllocation, $"{line.FgCode} · {Label(line.ColorSize)}", l.Qty, max, $"Maksimal {max} (min Dialokasikan, Qty − Terkirim).")); }
        }

        return violations;
    }

    private void Apply(Delivery d, int sign)
    {
        var po = Po(d.PoId);
        foreach (var (lineId, qty) in d.Lines)
        {
            var line = po.Lines.First(l => l.Id == lineId);
            _pos.AdjustStock(line.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId, -sign * qty);
            _pos.AdjustCommitted(lineId, deltaDelivered: sign * qty, deltaAllocated: -sign * qty);
            _inventory.RecordDelivery(line.FgId, line.ColorSize.ColorId, line.ColorSize.SizeId, d.Date, d.Id, d.Code, -sign * qty);
        }
    }

    private DeliveryDto Dto(Delivery d)
    {
        var po = Po(d.PoId);
        var lines = d.Lines.Select(l => { var pl = po.Lines.First(x => x.Id == l.Key); return new DeliveryLineDto(l.Key, pl.FgCode, pl.FgName, pl.ColorSize, l.Value); }).ToList();
        var active = d.Status == DocStatus.Active;
        var allowed = new List<string>();
        if (active)
        {
            allowed.Add(DeliveryActions.EditInfo);
            allowed.Add(DeliveryActions.Print);
            if (po.Status != PoStatus.Cancelled) { allowed.Add(DeliveryActions.Void); allowed.Add(DeliveryActions.Correct); }
        }
        else { allowed.Add(DeliveryActions.Print); }

        return new DeliveryDto(Item(d), d.Recipient, d.Vehicle, d.Driver, d.Notes, lines, d.VoidReason, d.ReplacesId, d.ReplacedById, allowed, d.Version);
    }

    private DeliveryListItem Item(Delivery d)
    {
        var po = Po(d.PoId);
        return new DeliveryListItem(d.Id, d.Code, d.Date, d.PoId, po.Code, po.CustomerPoNumber, po.CustomerName, d.Lines.Values.Sum(), d.Status, d.Prints, "Administrator");
    }

    public Task<DeliveryDto> CreateAsync(SaveDeliveryRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var po = Po(r.PoId);
            if (po.Status is not (PoStatus.Confirmed or PoStatus.InProgress)) { throw Problem(ProblemTypes.StateConflict, 409, "PO tidak dapat dikirim.", $"PO berstatus {po.Status}."); }
            if (r.DeliveryDate > Today()) { throw Invalid("deliveryDate", "Tanggal kirim tidak boleh di masa depan."); }
            if (r.Lines is null || r.Lines.All(l => l.Qty <= 0)) { throw Invalid("lines", "Isi minimal satu baris."); }
            var violations = Check(po, r.Lines, null);
            if (violations.Count > 0)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Qty kirim melebihi batas.", $"Batas terbaru: {violations[0].Limit}.", ProblemCodes.DeliveryExceedsAllocation, violations);
            }

            var d = new Delivery { Code = NextCode(r.DeliveryDate), PoId = r.PoId, Date = r.DeliveryDate, Recipient = Clean(r.RecipientName), Vehicle = Clean(r.Vehicle), Driver = Clean(r.Driver), Notes = Clean(r.Notes), Status = DocStatus.Active };
            foreach (var l in r.Lines.Where(l => l.Qty > 0)) { d.Lines[l.PoLineId] = l.Qty; }
            _deliveries.Add(d);
            Apply(d, 1);
            return Task.FromResult(Dto(d));
        }
    }

    public Task<PagedResult<DeliveryListItem>> ListAsync(DeliveryListQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IEnumerable<Delivery> rows = _deliveries;
            if (!string.IsNullOrWhiteSpace(q.Q))
            {
                var s = q.Q.Trim();
                rows = rows.Where(d => { var i = Item(d); return d.Code.Contains(s, StringComparison.OrdinalIgnoreCase) || i.PoCode.Contains(s, StringComparison.OrdinalIgnoreCase) || i.CustomerName.Contains(s, StringComparison.OrdinalIgnoreCase); });
            }

            if (q.PoId is { } po) { rows = rows.Where(d => d.PoId == po); }
            if (q.CustomerId is { } c) { rows = rows.Where(d => Po(d.PoId).CustomerId == c); }
            if (q.From is { } f) { rows = rows.Where(d => d.Date >= f); }
            if (q.To is { } t) { rows = rows.Where(d => d.Date <= t); }
            rows = q.Status is { } st ? rows.Where(d => d.Status == st) : rows;
            var all = rows.OrderByDescending(d => d.Code).ToList();
            return Task.FromResult(new PagedResult<DeliveryListItem>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(Item).ToList(), q.Page, q.PageSize, all.Count));
        }
    }

    private Delivery Find(Guid id) => _deliveries.FirstOrDefault(d => d.Id == id) ?? throw Problem(ProblemTypes.NotFound, 404, "Surat jalan tidak ditemukan.");

    public Task<DeliveryDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(Dto(Find(id))); }
    }

    public Task<DeliveryDto> UpdateInfoAsync(Guid id, UpdateDeliveryInfoRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = Find(id);
            if (d.Version != r.RowVersion) { throw Problem(ProblemTypes.ConcurrencyConflict, 409, "Data telah diubah pengguna lain.", "Muat ulang halaman lalu ulangi."); }
            if (d.Status != DocStatus.Active) { throw Problem(ProblemTypes.StateConflict, 409, "Surat jalan sudah di-void."); }
            if (r.DeliveryDate > Today()) { throw Invalid("deliveryDate", "Tanggal kirim tidak boleh di masa depan."); }
            (d.Date, d.Recipient, d.Vehicle, d.Driver, d.Notes) = (r.DeliveryDate, Clean(r.RecipientName), Clean(r.Vehicle), Clean(r.Driver), Clean(r.Notes));
            d.Version++;
            return Task.FromResult(Dto(d));
        }
    }

    public Task<ImpactPreview> PreviewVoidAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = Find(id);
            var po = Po(d.PoId);
            var rows = d.Lines.Select(l => { var pl = po.Lines.First(x => x.Id == l.Key); return new ImpactRow($"{pl.FgCode} · {Label(pl.ColorSize)}", "Terkirim", pl.DeliveredQty, pl.DeliveredQty - l.Value, null, true, null); }).ToList();
            var notes = new List<string> { $"Stok fisik +{d.Lines.Values.Sum()} pcs dikembalikan; alokasi PO kembali aktif." };
            if (po.Status == PoStatus.Completed) { notes.Add("Status PO: Completed → In Progress."); }
            return Task.FromResult(new ImpactPreview(rows, d.Status == DocStatus.Active && po.Status != PoStatus.Cancelled, [], notes));
        }
    }

    public Task<DeliveryDto> VoidAsync(Guid id, ReasonRequest request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = Find(id);
            if (string.IsNullOrWhiteSpace(request.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            if (d.Status != DocStatus.Active) { throw Problem(ProblemTypes.StateConflict, 409, "Surat jalan sudah di-void."); }
            if (Po(d.PoId).Status == PoStatus.Cancelled) { throw Problem(ProblemTypes.StateConflict, 409, "PO dibatalkan; surat jalan tidak dapat di-void."); }
            Apply(d, -1);
            d.Status = DocStatus.Voided;
            d.VoidReason = request.Reason.Trim();
            d.Version++;
            return Task.FromResult(Dto(d));
        }
    }

    public Task<ImpactPreview> PreviewCorrectAsync(Guid id, CorrectDeliveryRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = Find(id);
            var po = Po(d.PoId);
            var violations = Check(po, r.Lines, d);
            var rows = r.Lines.Where(l => l.Qty > 0).Select(l =>
            {
                var pl = po.Lines.First(x => x.Id == l.PoLineId);
                var back = d.Lines.GetValueOrDefault(pl.Id);
                var v = violations.FirstOrDefault(x => x.Subject.EndsWith(Label(pl.ColorSize), StringComparison.Ordinal));
                return new ImpactRow($"{pl.FgCode} · {Label(pl.ColorSize)}", "Terkirim", pl.DeliveredQty, pl.DeliveredQty - back + l.Qty, v?.Limit, v is null, v?.Message);
            }).ToList();
            return Task.FromResult(new ImpactPreview(rows, violations.Count == 0, violations, []));
        }
    }

    public Task<DeliveryDto> CorrectAsync(Guid id, CorrectDeliveryRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = Find(id);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            if (d.Status != DocStatus.Active) { throw Problem(ProblemTypes.StateConflict, 409, "Surat jalan sudah di-void."); }
            var po = Po(d.PoId);
            if (po.Status == PoStatus.Cancelled) { throw Problem(ProblemTypes.StateConflict, 409, "PO dibatalkan; surat jalan tidak dapat dikoreksi."); }
            if (r.DeliveryDate > Today()) { throw Invalid("deliveryDate", "Tanggal kirim tidak boleh di masa depan."); }
            if (r.Lines.All(l => l.Qty <= 0)) { throw Invalid("lines", "Isi minimal satu baris."); }
            var violations = Check(po, r.Lines, d);
            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Koreksi melebihi batas.", code: ProblemCodes.DeliveryExceedsAllocation, violations: violations); }
            Apply(d, -1);
            d.Status = DocStatus.Voided;
            d.VoidReason = r.Reason.Trim();
            d.Version++;
            var n = new Delivery { Code = NextCode(r.DeliveryDate), PoId = d.PoId, Date = r.DeliveryDate, Recipient = Clean(r.RecipientName), Vehicle = Clean(r.Vehicle), Driver = Clean(r.Driver), Notes = Clean(r.Notes), Status = DocStatus.Active, ReplacesId = d.Id };
            foreach (var l in r.Lines.Where(l => l.Qty > 0)) { n.Lines[l.PoLineId] = l.Qty; }
            d.ReplacedById = n.Id;
            _deliveries.Add(n);
            Apply(n, 1);
            return Task.FromResult(Dto(n));
        }
    }

    public Task<FileContent> GetPdfAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var d = Find(id);
            d.Prints++;
            var text = $"%PDF-1.4\n% Surat Jalan {d.Code}{(d.Status == DocStatus.Voided ? " DIBATALKAN" : string.Empty)}{(d.Prints > 1 ? " CETAK ULANG" : string.Empty)}\n%%EOF\n";
            return Task.FromResult(new FileContent($"{d.Code}.pdf", "application/pdf", Encoding.ASCII.GetBytes(text)));
        }
    }
}
