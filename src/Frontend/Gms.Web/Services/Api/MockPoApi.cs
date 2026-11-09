using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Problems;

namespace Gms.Web.Services.Api;

/// <summary>
/// Customer PO tiruan (ADR-014) dengan aturan API: kode otomatis, Draft→Confirmed→(In Progress/Completed)/Cancelled, revisi dengan batas minimum Qty,
/// baris terkunci, alokasi stok = min(Tersedia, Sisa Rencana), nomor PO customer unik per customer. Metrik dihitung "server" (di sini), bukan di UI.
/// Modul lain (WO, Pengiriman) dapat memutakhirkan <c>InProduction</c>/<c>Delivered</c> lewat metode internal.
/// </summary>
public sealed class MockPoApi : IPurchaseOrdersApi
{
    private sealed class Line
    {
        public Guid Id = Guid.NewGuid(); public Guid FgId; public Guid ColorId; public Guid SizeId; public DateOnly Delivery; public int Qty;
        public int Delivered, Allocated, InProduction; public string? Notes;
    }

    private sealed class Po
    {
        public Guid Id = Guid.NewGuid(); public string Code = ""; public string? CustomerPoNumber; public Guid CustomerId; public DateOnly Date; public PoStatus Status;
        public string? Notes, CancelReason; public List<Line> Lines = []; public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow; public uint Version = 1;
    }

    private readonly object _gate = new();
    private readonly List<Po> _pos = [];
    private readonly List<AllocationEntryDto> _entries = [];
    private readonly Dictionary<(Guid Fg, Guid Color, Guid Size), int> _onHand = [];
    private long _entryId = 1;
    private readonly MockCustomerApi _customers;
    private readonly MockFgApi _fgs;
    private readonly MockColorApi _colors;
    private readonly MockSizeApi _sizes;

    /// <summary>Tanggal "hari ini" (WIB) untuk penanda Terlambat; dapat diganti pada tes.</summary>
    public Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

    public MockPoApi(MockCustomerApi customers, MockFgApi fgs, MockColorApi colors, MockSizeApi sizes)
    {
        _customers = customers;
        _fgs = fgs;
        _colors = colors;
        _sizes = sizes;
        Seed();
    }

    public static readonly Guid SeedPoId = Guid.Parse("00000000-0000-0000-0000-00000000c001");

    private void Seed()
    {
        var customer = _customers.LookupAsync(null).Result[0];
        var navy = _colors.LookupAsync("NVY").Result[0];
        var sizes = _sizes.ListAsync(new Gms.Shared.Master.MasterListQuery()).Result.Items;
        var today = Today();
        var po = new Po { Id = SeedPoId, Code = "PO-202611-0001", CustomerPoNumber = "PO/25/118", CustomerId = customer.Id, Date = today.AddDays(-8), Status = PoStatus.Confirmed };
        var qtys = new[] { 100, 200, 150, 50 };
        for (var i = 0; i < sizes.Count; i++)
        {
            po.Lines.Add(new Line { FgId = MockFgApi.KemejaId, ColorId = navy.Id, SizeId = sizes[i].Id, Delivery = today.AddDays(12), Qty = qtys[i] });
            _onHand[(MockFgApi.KemejaId, navy.Id, sizes[i].Id)] = 120;
        }

        _pos.Add(po);
        _pos.Add(new Po
        {
            Code = "PO-202611-0002", CustomerId = customer.Id, Date = today.AddDays(-1), Status = PoStatus.Draft,
            Lines = [new Line { FgId = MockFgApi.KemejaId, ColorId = navy.Id, SizeId = sizes[1].Id, Delivery = today.AddDays(20), Qty = 80 }],
        });
    }

    // ----- Penghitung & proyeksi -----

    private static int Remaining(Line l) => Math.Max(0, l.Qty - l.Delivered - l.Allocated - l.InProduction);

    private static int MinAllowed(Line l) => l.Delivered + l.Allocated + l.InProduction;

    private static bool Locked(Line l) => l.Delivered + l.Allocated + l.InProduction > 0;

    private PoLineDto Dto(Po po, Line l)
    {
        var fg = _fgs.GetAsync(l.FgId).Result;
        var color = _colors.GetAsync(l.ColorId).Result;
        var size = _sizes.GetAsync(l.SizeId).Result;
        var late = po.Status is PoStatus.Confirmed or PoStatus.InProgress && l.Delivery < Today() && l.Qty > l.Delivered;
        return new PoLineDto(l.Id, fg.Id, fg.Code, fg.Name, new ColorSizeRef(color.Id, color.Code, color.Name, size.Id, size.Code, size.Name, size.SortOrder),
            l.Delivery, l.Qty, l.Delivered, l.Allocated, l.InProduction, Remaining(l), MinAllowed(l), Locked(l), late, l.Notes);
    }

    private static IReadOnlyList<string> Actions(Po po) => po.Status switch
    {
        PoStatus.Draft => [PoActions.Edit, PoActions.Confirm, PoActions.Cancel],
        PoStatus.Confirmed or PoStatus.InProgress =>
            [PoActions.Edit, PoActions.SetCustomerPoNumber, PoActions.Cancel, PoActions.Allocate, PoActions.CreateWorkOrder, PoActions.CreateDelivery],
        _ => [],
    };

    private PoDto Dto(Po po)
    {
        var lines = po.Lines.OrderBy(l => _fgs.GetAsync(l.FgId).Result.Code).ThenBy(l => _colors.GetAsync(l.ColorId).Result.Name).ThenBy(l => l.Delivery)
            .ThenBy(l => _sizes.GetAsync(l.SizeId).Result.SortOrder).Select(l => Dto(po, l)).ToList();
        return new PoDto(po.Id, po.Code, po.CustomerPoNumber, po.CustomerId, _customers.GetAsync(po.CustomerId).Result.Name, po.Date, po.Status, po.Notes, po.CancelReason,
            lines, lines.Sum(l => l.Qty), lines.Sum(l => l.DeliveredQty), lines.Sum(l => l.AllocatedQty), lines.Sum(l => l.InProductionQty), lines.Sum(l => l.RemainingPlanQty),
            Actions(po), po.CreatedAt, "Administrator", po.Version);
    }

    private static ApiException Problem(string type, int status, string title, string? detail = null, string? code = null, IReadOnlyList<RuleViolation>? violations = null,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, Code = code, Violations = violations, Errors = errors, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    private static ApiException Invalid(string path, string message) =>
        Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: new Dictionary<string, string[]> { [path] = [message] });

    private Po Find(Guid id) => _pos.FirstOrDefault(p => p.Id == id) ?? throw Problem(ProblemTypes.NotFound, 404, "PO tidak ditemukan.");

    private (Po Po, Line Line) FindLine(Guid lineId)
    {
        foreach (var po in _pos)
        {
            if (po.Lines.FirstOrDefault(l => l.Id == lineId) is { } line) { return (po, line); }
        }

        throw Problem(ProblemTypes.NotFound, 404, "Baris PO tidak ditemukan.");
    }

    private static void CheckVersion(Po po, uint version)
    {
        if (po.Version != version)
        {
            throw Problem(ProblemTypes.ConcurrencyConflict, 409, "PO telah diubah pengguna lain.", "Muat ulang halaman lalu ulangi perubahan Anda.");
        }
    }

    private static void RequireState(Po po, params PoStatus[] allowed)
    {
        if (!allowed.Contains(po.Status))
        {
            throw Problem(ProblemTypes.StateConflict, 409, "Status PO tidak mengizinkan aksi ini.", $"PO berstatus {po.Status}.");
        }
    }

    // ----- Daftar -----

    public Task<PagedResult<PoListItem>> ListAsync(PoListQuery q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var today = Today();
            IEnumerable<Po> rows = _pos;
            if (!string.IsNullOrWhiteSpace(q.Q))
            {
                var s = q.Q.Trim();
                rows = rows.Where(p => p.Code.Contains(s, StringComparison.OrdinalIgnoreCase) || (p.CustomerPoNumber?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                    || _customers.GetAsync(p.CustomerId).Result.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            if (q.Status is { } status) { rows = rows.Where(p => p.Status == status); }
            if (q.CustomerId is { } cid) { rows = rows.Where(p => p.CustomerId == cid); }
            if (q.From is { } from) { rows = rows.Where(p => p.Date >= from); }
            if (q.To is { } to) { rows = rows.Where(p => p.Date <= to); }
            bool Active(Po p) => p.Status is PoStatus.Confirmed or PoStatus.InProgress;
            if (q.Terlambat) { rows = rows.Where(p => Active(p) && p.Lines.Any(l => l.Delivery < today && l.Qty > l.Delivered)); }
            if (q.JatuhTempo7) { rows = rows.Where(p => Active(p) && p.Lines.Any(l => l.Delivery >= today && l.Delivery <= today.AddDays(7) && l.Qty > l.Delivered)); }
            if (q.PoCustomerKosong) { rows = rows.Where(p => Active(p) && string.IsNullOrEmpty(p.CustomerPoNumber)); }
            if (q.SisaRencana) { rows = rows.Where(p => Active(p) && p.Lines.Any(l => Remaining(l) > 0)); }
            var all = rows.OrderByDescending(p => p.Date).ThenByDescending(p => p.Code).ToList();
            var items = all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(p =>
                new PoListItem(p.Id, p.Code, p.CustomerPoNumber, p.CustomerId, _customers.GetAsync(p.CustomerId).Result.Name, p.Date, p.Status,
                    p.Lines.Sum(l => l.Qty), p.Lines.Sum(l => l.Delivered), p.Lines.Sum(l => l.Allocated), p.Lines.Sum(l => l.InProduction),
                    Active(p) && p.Lines.Any(l => l.Delivery < today && l.Qty > l.Delivered))).ToList();
            return Task.FromResult(new PagedResult<PoListItem>(items, q.Page, q.PageSize, all.Count));
        }
    }

    /// <summary>Jumlah per filter cepat (angka pada chip dihitung server).</summary>
    public Task<IReadOnlyDictionary<string, int>> QuickCountsAsync()
    {
        IReadOnlyDictionary<string, int> counts = new Dictionary<string, int>
        {
            ["terlambat"] = ListAsync(new PoListQuery { Terlambat = true, PageSize = 1 }).Result.TotalCount,
            ["jatuhTempo7"] = ListAsync(new PoListQuery { JatuhTempo7 = true, PageSize = 1 }).Result.TotalCount,
            ["poCustomerKosong"] = ListAsync(new PoListQuery { PoCustomerKosong = true, PageSize = 1 }).Result.TotalCount,
            ["sisaRencana"] = ListAsync(new PoListQuery { SisaRencana = true, PageSize = 1 }).Result.TotalCount,
        };
        return Task.FromResult(counts);
    }

    public Task<PoDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(Dto(Find(id))); }
    }

    // ----- Simpan -----

    private void ValidateRequest(SavePoRequest r, Guid? self)
    {
        if (!_customers.Contains(r.CustomerId)) { throw Invalid("customerId", "Customer wajib dipilih."); }
        if (r.Lines is null || r.Lines.Count == 0) { throw Invalid("lines", "Minimal satu baris kebutuhan."); }
        var seen = new HashSet<(Guid, Guid, Guid, DateOnly)>();
        for (var i = 0; i < r.Lines.Count; i++)
        {
            var l = r.Lines[i];
            if (l.Qty <= 0) { throw Invalid($"lines[{i}].qty", "Qty harus lebih dari 0."); }
            if (l.Qty > 1_000_000) { throw Invalid($"lines[{i}].qty", "Qty maksimal 1.000.000."); }
            if (!_fgs.Contains(l.FgId)) { throw Invalid($"lines[{i}].fgId", "FG tidak ditemukan."); }
            if (!_colors.Contains(l.ColorId)) { throw Invalid($"lines[{i}].colorId", "Color tidak ditemukan."); }
            if (!_fgs.GetSizesAsync(l.FgId).Result.SizeIds.Contains(l.SizeId)) { throw Invalid($"lines[{i}].sizeId", "Size bukan Size Berlaku FG ini."); }
            if (!seen.Add((l.FgId, l.ColorId, l.SizeId, l.DeliveryDate))) { throw Invalid($"lines[{i}]", "Kombinasi FG, Color, Size, dan Tanggal Kirim ganda."); }
        }

        if (!string.IsNullOrWhiteSpace(r.CustomerPoNumber)
            && _pos.FirstOrDefault(p => p.Id != self && p.CustomerId == r.CustomerId && string.Equals(p.CustomerPoNumber, r.CustomerPoNumber.Trim(), StringComparison.OrdinalIgnoreCase)) is { } dup)
        {
            throw Problem(ProblemTypes.Duplicate, 409, "Nomor PO customer duplikat.", $"Nomor PO ini sudah dipakai {dup.Code} untuk customer yang sama.",
                errors: new Dictionary<string, string[]> { ["customerPoNumber"] = [$"Nomor PO ini sudah dipakai {dup.Code} untuk customer yang sama."] });
        }
    }

    private string NextCode(DateOnly date)
    {
        var prefix = $"PO-{date:yyyyMM}-";
        var next = _pos.Where(p => p.Code.StartsWith(prefix, StringComparison.Ordinal)).Select(p => int.Parse(p.Code[prefix.Length..])).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }

    public Task<PoDto> CreateAsync(SavePoRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ValidateRequest(r, null);
            var po = new Po
            {
                Code = NextCode(r.PoDate), CustomerId = r.CustomerId, Date = r.PoDate, CustomerPoNumber = Clean(r.CustomerPoNumber), Notes = Clean(r.Notes),
                Status = r.ConfirmAfterSave ? PoStatus.Confirmed : PoStatus.Draft,
                Lines = r.Lines.Select(l => new Line { FgId = l.FgId, ColorId = l.ColorId, SizeId = l.SizeId, Delivery = l.DeliveryDate, Qty = l.Qty, Notes = Clean(l.Notes) }).ToList(),
            };
            _pos.Add(po);
            return Task.FromResult(Dto(po));
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public Task<PoDto> UpdateAsync(Guid id, SavePoRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var po = Find(id);
            CheckVersion(po, r.RowVersion);
            RequireState(po, PoStatus.Draft, PoStatus.Confirmed, PoStatus.InProgress);
            ValidateRequest(r, id);
            var keep = new HashSet<Guid>();
            var violations = new List<RuleViolation>();
            foreach (var l in r.Lines)
            {
                var existing = l.Id is { } lid ? po.Lines.FirstOrDefault(x => x.Id == lid) : null;
                if (existing is null) { continue; }
                keep.Add(existing.Id);
                if (Locked(existing) && (existing.FgId != l.FgId || existing.ColorId != l.ColorId || existing.SizeId != l.SizeId))
                {
                    throw Problem(ProblemTypes.RuleViolation, 409, "Baris sudah dipakai; FG/Color/Size terkunci.", code: ProblemCodes.PoLineInUse);
                }

                if (l.Qty < MinAllowed(existing))
                {
                    var size = _sizes.GetAsync(l.SizeId).Result.Code;
                    violations.Add(new RuleViolation(ProblemCodes.PoQtyBelowCommitted, $"{_fgs.GetAsync(l.FgId).Result.Code} · {_colors.GetAsync(l.ColorId).Result.Name} · {size}", l.Qty, MinAllowed(existing),
                        $"Qty minimal {MinAllowed(existing)} (Terkirim {existing.Delivered} + Dialokasikan {existing.Allocated} + Dalam Produksi {existing.InProduction}). Lepas alokasi atau ubah WO terlebih dahulu."));
                }
            }

            if (violations.Count > 0) { throw Problem(ProblemTypes.RuleViolation, 409, "Qty di bawah komitmen.", code: ProblemCodes.PoQtyBelowCommitted, violations: violations); }
            if (po.Lines.Any(x => !keep.Contains(x.Id) && Locked(x))) { throw Problem(ProblemTypes.RuleViolation, 409, "Baris yang sudah dipakai tidak dapat dihapus.", code: ProblemCodes.PoLineInUse); }

            po.CustomerId = r.CustomerId;
            po.Date = r.PoDate;
            po.CustomerPoNumber = Clean(r.CustomerPoNumber);
            po.Notes = Clean(r.Notes);
            var updated = new List<Line>();
            foreach (var l in r.Lines)
            {
                var existing = l.Id is { } lid ? po.Lines.FirstOrDefault(x => x.Id == lid) : null;
                if (existing is null) { updated.Add(new Line { FgId = l.FgId, ColorId = l.ColorId, SizeId = l.SizeId, Delivery = l.DeliveryDate, Qty = l.Qty, Notes = Clean(l.Notes) }); continue; }
                (existing.FgId, existing.ColorId, existing.SizeId, existing.Delivery, existing.Qty, existing.Notes) = (l.FgId, l.ColorId, l.SizeId, l.DeliveryDate, l.Qty, Clean(l.Notes));
                updated.Add(existing);
            }

            po.Lines = updated;
            po.Version++;
            return Task.FromResult(Dto(po));
        }
    }

    public Task<PoDto> SetCustomerPoNumberAsync(Guid id, SetCustomerPoNumberRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var po = Find(id);
            CheckVersion(po, r.RowVersion);
            RequireState(po, PoStatus.Draft, PoStatus.Confirmed, PoStatus.InProgress);
            var number = Clean(r.CustomerPoNumber);
            if (number is not null && _pos.FirstOrDefault(p => p.Id != id && p.CustomerId == po.CustomerId && string.Equals(p.CustomerPoNumber, number, StringComparison.OrdinalIgnoreCase)) is { } dup)
            {
                throw Problem(ProblemTypes.Duplicate, 409, "Nomor PO customer duplikat.", $"Nomor PO ini sudah dipakai {dup.Code} untuk customer yang sama.",
                    errors: new Dictionary<string, string[]> { ["customerPoNumber"] = [$"Nomor PO ini sudah dipakai {dup.Code} untuk customer yang sama."] });
            }

            po.CustomerPoNumber = number;
            po.Version++;
            return Task.FromResult(Dto(po));
        }
    }

    public Task<PoDto> ConfirmAsync(Guid id, uint rowVersion, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var po = Find(id);
            CheckVersion(po, rowVersion);
            RequireState(po, PoStatus.Draft);
            po.Status = PoStatus.Confirmed;
            po.Version++;
            return Task.FromResult(Dto(po));
        }
    }

    public Task<PoDto> CancelAsync(Guid id, ReasonRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var po = Find(id);
            CheckVersion(po, r.RowVersion);
            RequireState(po, PoStatus.Draft, PoStatus.Confirmed, PoStatus.InProgress);
            if (string.IsNullOrWhiteSpace(r.Reason)) { throw Invalid("reason", "Alasan wajib diisi."); }
            if (po.Lines.Any(l => l.Delivered > 0 || l.InProduction > 0))
            {
                var blockers = po.Lines.Where(l => l.Delivered > 0 || l.InProduction > 0)
                    .Select(l => new RuleViolation("po.cancel-blocked", $"{_colors.GetAsync(l.ColorId).Result.Name} · {_sizes.GetAsync(l.SizeId).Result.Code}", l.Delivered + l.InProduction, 0,
                        "Sudah ada pengiriman atau Work Order aktif.")).ToList();
                throw Problem(ProblemTypes.RuleViolation, 409, "PO tidak dapat dibatalkan.", code: "po.cancel-blocked", violations: blockers);
            }

            foreach (var l in po.Lines.Where(x => x.Allocated > 0))
            {
                Log(po, l, AllocationKind.Release, l.Allocated, AllocationSource.PoCancel, "Pembatalan PO");
                l.Allocated = 0;
            }

            po.Status = PoStatus.Cancelled;
            po.CancelReason = r.Reason.Trim();
            po.Version++;
            return Task.FromResult(Dto(po));
        }
    }

    // ----- Pengait untuk modul lain (WO, Pengiriman, Serah Terima) -----

    /// <summary>Memutakhirkan metrik baris dari modul lain (Dalam Produksi dari WO, Terkirim dari Pengiriman) dan menyesuaikan status PO.</summary>
    public void AdjustCommitted(Guid poLineId, int deltaDelivered = 0, int deltaInProduction = 0, int deltaAllocated = 0)
    {
        lock (_gate)
        {
            var (po, l) = FindLine(poLineId);
            l.Delivered += deltaDelivered;
            l.InProduction += deltaInProduction;
            l.Allocated += deltaAllocated;
            if (po.Status == PoStatus.Confirmed && MinAllowed(l) > 0) { po.Status = PoStatus.InProgress; }
            if (po.Status == PoStatus.InProgress && po.Lines.All(x => x.Delivered >= x.Qty)) { po.Status = PoStatus.Completed; }
            po.Version++;
        }
    }

    /// <summary>Menambah/mengurangi stok fisik FG (dipakai Serah Terima).</summary>
    public void AdjustStock(Guid fgId, Guid colorId, Guid sizeId, int delta)
    {
        lock (_gate) { _onHand[(fgId, colorId, sizeId)] = _onHand.GetValueOrDefault((fgId, colorId, sizeId)) + delta; }
    }

    // ----- Alokasi -----

    private void Log(Po po, Line l, AllocationKind kind, int qty, AllocationSource source, string? reason)
    {
        var fg = _fgs.GetAsync(l.FgId).Result;
        var color = _colors.GetAsync(l.ColorId).Result;
        var size = _sizes.GetAsync(l.SizeId).Result;
        _entries.Add(new AllocationEntryDto(_entryId++, l.Id, fg.Code, new ColorSizeRef(color.Id, color.Code, color.Name, size.Id, size.Code, size.Name, size.SortOrder),
            kind, kind == AllocationKind.Release ? -qty : qty, source, reason, DateTimeOffset.UtcNow, "Administrator"));
    }

    public Task<IReadOnlyList<AllocationEntryDto>> GetAllocationsAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var ids = Find(id).Lines.Select(l => l.Id).ToHashSet();
            return Task.FromResult<IReadOnlyList<AllocationEntryDto>>(_entries.Where(e => ids.Contains(e.PoLineId)).OrderByDescending(e => e.Id).ToList());
        }
    }

    private StockAvailabilityDto Availability(Line l)
    {
        var onHand = _onHand.GetValueOrDefault((l.FgId, l.ColorId, l.SizeId));
        var allocatedAll = _pos.SelectMany(p => p.Lines).Where(x => x.FgId == l.FgId && x.ColorId == l.ColorId && x.SizeId == l.SizeId).Sum(x => x.Allocated);
        var available = Math.Max(0, onHand - allocatedAll);
        var max = Math.Min(available, Remaining(l));
        var reason = max > 0 ? null : Remaining(l) == 0 ? "Sisa Rencana habis." : "Stok tersedia tidak ada.";
        return new StockAvailabilityDto(onHand, allocatedAll, available, Remaining(l), l.Allocated, max, reason);
    }

    public Task<StockAvailabilityDto> GetStockAvailabilityAsync(Guid poLineId, CancellationToken ct = default)
    {
        lock (_gate) { return Task.FromResult(Availability(FindLine(poLineId).Line)); }
    }

    public Task<PoLineDto> AllocateAsync(Guid poLineId, AllocateRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var (po, l) = FindLine(poLineId);
            RequireState(po, PoStatus.Confirmed, PoStatus.InProgress);
            if (r.Qty <= 0) { throw Invalid("qty", "Qty alokasi harus lebih dari 0."); }
            var a = Availability(l);
            if (r.Qty > a.MaxAllocatable)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Qty alokasi melebihi batas.", $"Stok tersedia berubah menjadi {a.MaxAllocatable}. Sesuaikan jumlah.", ProblemCodes.AllocationExceeded,
                    [new RuleViolation(ProblemCodes.AllocationExceeded, "Alokasi", r.Qty, a.MaxAllocatable, a.LimitReason ?? $"Maksimal {a.MaxAllocatable}.")]);
            }

            l.Allocated += r.Qty;
            Log(po, l, AllocationKind.Allocate, r.Qty, AllocationSource.Manual, Clean(r.Reason));
            if (po.Status == PoStatus.Confirmed) { po.Status = PoStatus.InProgress; }
            po.Version++;
            return Task.FromResult(Dto(po, l));
        }
    }

    public Task<PoLineDto> ReleaseAllocationAsync(Guid poLineId, ReleaseAllocationRequest r, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var (po, l) = FindLine(poLineId);
            RequireState(po, PoStatus.Confirmed, PoStatus.InProgress);
            if (r.Qty <= 0) { throw Invalid("qty", "Qty pelepasan harus lebih dari 0."); }
            if (r.Qty > l.Allocated)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Qty pelepasan melebihi alokasi.", code: ProblemCodes.AllocationExceeded,
                    violations: [new RuleViolation(ProblemCodes.AllocationExceeded, "Pelepasan", r.Qty, l.Allocated, $"Maksimal {l.Allocated}.")]);
            }

            l.Allocated -= r.Qty;
            Log(po, l, AllocationKind.Release, r.Qty, AllocationSource.Manual, Clean(r.Reason));
            po.Version++;
            return Task.FromResult(Dto(po, l));
        }
    }
}
