using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;

namespace Gms.Web.Services.Api;

/// <summary>
/// CRUD generik tiruan (ADR-014): meniru API — kode unik (409), nama wajib (422), rowVersion basi (409), nonaktif tanpa hapus, lookup hanya aktif.
/// Status disimpan di instance (didaftarkan sebagai singleton) sehingga data bertahan selama proses berjalan.
/// </summary>
public abstract class MockCrud<TItem, TDetail, TSave> : IMasterCrudApi<TItem, TDetail, TSave>
    where TDetail : class
{
    private readonly object _gate = new();
    private readonly List<TDetail> _rows = [];

    protected abstract Guid IdOf(TDetail d);
    protected abstract string CodeOf(TDetail d);
    protected abstract string NameOf(TDetail d);
    protected abstract bool ActiveOf(TDetail d);
    protected abstract uint VersionOf(TDetail d);
    protected abstract TDetail WithActive(TDetail d, bool active);
    protected abstract TItem ToItem(TDetail d);
    protected abstract TDetail Build(Guid id, string code, TSave request);
    protected abstract TDetail Apply(TDetail d, string code, TSave request);
    protected abstract string? RequestedCode(TSave request);
    protected abstract string RequestedName(TSave request);
    protected abstract uint RequestedVersion(TSave request);

    /// <summary>Awalan kode otomatis (entitas yang kodenya dibuat sistem).</summary>
    protected virtual string AutoCodePrefix => "X";

    protected virtual int CodeMax => 30;

    protected virtual ApiException? Validate(TSave request) => null;

    protected virtual ApiException? CheckDeactivate(TDetail d) => null;

    protected virtual IEnumerable<TDetail> Order(IEnumerable<TDetail> rows) => rows.OrderBy(CodeOf, StringComparer.OrdinalIgnoreCase);

    protected virtual bool Matches(TDetail d, string q) =>
        CodeOf(d).Contains(q, StringComparison.OrdinalIgnoreCase) || NameOf(d).Contains(q, StringComparison.OrdinalIgnoreCase);

    protected void Seed(params TDetail[] rows) => _rows.AddRange(rows);

    public bool Contains(Guid id)
    {
        lock (_gate)
        {
            return _rows.Any(r => IdOf(r) == id);
        }
    }

    protected IReadOnlyList<TDetail> Snapshot()
    {
        lock (_gate)
        {
            return _rows.ToList();
        }
    }

    protected static ApiException Invalid(string field, string message) =>
        MockAccessStore.Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.",
            errors: new Dictionary<string, string[]> { [field] = [message] });

    private static ApiException NotFound() => MockAccessStore.Problem(ProblemTypes.NotFound, 404, "Data tidak ditemukan.");

    private static ApiException Stale() =>
        MockAccessStore.Problem(ProblemTypes.ConcurrencyConflict, 409, "Data telah diubah pengguna lain.", "Muat ulang halaman lalu ulangi perubahan Anda.");

    private ApiException? Check(TSave request, Guid? self, string? code)
    {
        if (string.IsNullOrWhiteSpace(RequestedName(request))) { return Invalid("name", "Nama wajib diisi."); }
        if (code is not null)
        {
            if (code.Length == 0) { return Invalid("code", "Kode wajib diisi."); }
            if (code.Length > CodeMax) { return Invalid("code", $"Kode maksimal {CodeMax} karakter."); }
            if (_rows.Any(r => IdOf(r) != self && CodeOf(r).Equals(code, StringComparison.OrdinalIgnoreCase)))
            {
                return MockAccessStore.Problem(ProblemTypes.Duplicate, 409, "Kode sudah dipakai.", $"Kode \"{code}\" sudah terdaftar.");
            }
        }

        return Validate(request);
    }

    public Task<PagedResult<TItem>> ListAsync(MasterListQuery query, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var rows = _rows.AsEnumerable();
            rows = query.Status switch { ActiveFilter.Active => rows.Where(ActiveOf), ActiveFilter.Inactive => rows.Where(r => !ActiveOf(r)), _ => rows };
            if (!string.IsNullOrWhiteSpace(query.Q)) { rows = rows.Where(r => Matches(r, query.Q.Trim())); }
            var all = Order(rows).ToList();
            var items = all.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(ToItem).ToList();
            return Task.FromResult(new PagedResult<TItem>(items, query.Page, query.PageSize, all.Count));
        }
    }

    public Task<TDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_rows.FirstOrDefault(r => IdOf(r) == id) ?? throw NotFound());
        }
    }

    public Task<TDetail> CreateAsync(TSave request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var requested = RequestedCode(request)?.Trim();
            if (Check(request, null, requested) is { } problem) { throw problem; }
            var code = requested ?? $"{AutoCodePrefix}-{_rows.Count + 1:000}";
            var row = Build(Guid.NewGuid(), code, request);
            _rows.Add(row);
            return Task.FromResult(row);
        }
    }

    public Task<TDetail> UpdateAsync(Guid id, TSave request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var index = _rows.FindIndex(r => IdOf(r) == id);
            if (index < 0) { throw NotFound(); }
            if (VersionOf(_rows[index]) != RequestedVersion(request)) { throw Stale(); }
            var requested = RequestedCode(request)?.Trim();
            if (Check(request, id, requested) is { } problem) { throw problem; }
            _rows[index] = Apply(_rows[index], requested ?? CodeOf(_rows[index]), request);
            return Task.FromResult(_rows[index]);
        }
    }

    public Task<TDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var index = _rows.FindIndex(r => IdOf(r) == id);
            if (index < 0) { throw NotFound(); }
            if (VersionOf(_rows[index]) != rowVersion) { throw Stale(); }
            if (!isActive && CheckDeactivate(_rows[index]) is { } problem) { throw problem; }
            _rows[index] = WithActive(_rows[index], isActive);
            return Task.FromResult(_rows[index]);
        }
    }

    public Task<IReadOnlyList<LookupItem>> LookupAsync(string? q, CancellationToken ct = default)
    {
        lock (_gate)
        {
            IReadOnlyList<LookupItem> list = Order(_rows.Where(ActiveOf).Where(r => string.IsNullOrWhiteSpace(q) || Matches(r, q.Trim())))
                .Select(r => new LookupItem(IdOf(r), CodeOf(r), NameOf(r))).ToList();
            return Task.FromResult(list);
        }
    }
}

// ----- Bentuk sederhana: Satuan dan Kategori Material -----

public abstract class MockSimple : MockCrud<SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>
{
    protected override Guid IdOf(SimpleMasterDto d) => d.Id;
    protected override string CodeOf(SimpleMasterDto d) => d.Code;
    protected override string NameOf(SimpleMasterDto d) => d.Name;
    protected override bool ActiveOf(SimpleMasterDto d) => d.IsActive;
    protected override uint VersionOf(SimpleMasterDto d) => d.RowVersion;
    protected override SimpleMasterDto WithActive(SimpleMasterDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override SimpleMasterDto ToItem(SimpleMasterDto d) => d;
    protected override SimpleMasterDto Build(Guid id, string code, SaveSimpleMasterRequest r) => new(id, code, r.Name.Trim(), true, 1);
    protected override SimpleMasterDto Apply(SimpleMasterDto d, string code, SaveSimpleMasterRequest r) => d with { Code = code, Name = r.Name.Trim(), RowVersion = d.RowVersion + 1 };
    protected override string? RequestedCode(SaveSimpleMasterRequest r) => r.Code ?? string.Empty;
    protected override string RequestedName(SaveSimpleMasterRequest r) => r.Name;
    protected override uint RequestedVersion(SaveSimpleMasterRequest r) => r.RowVersion;

    protected static SimpleMasterDto Row(string code, string name, bool active = true) => new(Guid.NewGuid(), code, name, active, 1);
}

public sealed class MockUomApi : MockSimple, IUomApi
{
    public MockUomApi() => Seed(Row("PCS", "Pieces"), Row("MTR", "Meter"), Row("KG", "Kilogram"), Row("YRD", "Yard", active: false));
}

public sealed class MockMaterialCategoryApi : MockSimple, IMaterialCategoryApi
{
    public MockMaterialCategoryApi() => Seed(Row("KAIN", "Kain"), Row("BENANG", "Benang"), Row("AKS", "Aksesoris"));
}

// ----- Material -----

public sealed class MockMaterialApi : MockCrud<MaterialDto, MaterialDto, SaveMaterialRequest>, IMaterialApi
{
    private readonly MockUomApi _uoms;
    private readonly MockMaterialCategoryApi _categories;

    public MockMaterialApi(MockUomApi uoms, MockMaterialCategoryApi categories)
    {
        _uoms = uoms;
        _categories = categories;
        var cat = categories.LookupAsync(null).Result;
        var uom = uoms.LookupAsync(null).Result;
        Seed(
            new MaterialDto(Guid.NewGuid(), "KN-001", "Kain Drill Navy", cat[0].Id, cat[0].Name, uom[1].Id, uom[1].Code, "Drill 280gsm", true, 1),
            new MaterialDto(Guid.NewGuid(), "BN-001", "Benang Polyester 40/2", cat[1].Id, cat[1].Name, uom[0].Id, uom[0].Code, null, true, 1));
    }

    protected override Guid IdOf(MaterialDto d) => d.Id;
    protected override string CodeOf(MaterialDto d) => d.Code;
    protected override string NameOf(MaterialDto d) => d.Name;
    protected override bool ActiveOf(MaterialDto d) => d.IsActive;
    protected override uint VersionOf(MaterialDto d) => d.RowVersion;
    protected override MaterialDto WithActive(MaterialDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override MaterialDto ToItem(MaterialDto d) => d;
    protected override string? RequestedCode(SaveMaterialRequest r) => r.Code ?? string.Empty;
    protected override string RequestedName(SaveMaterialRequest r) => r.Name;
    protected override uint RequestedVersion(SaveMaterialRequest r) => r.RowVersion;

    protected override ApiException? Validate(SaveMaterialRequest r)
    {
        if (!_categories.Contains(r.CategoryId)) { return Invalid("categoryId", "Kategori wajib dipilih."); }
        return !_uoms.Contains(r.UomId) ? Invalid("uomId", "Satuan wajib dipilih.") : null;
    }

    protected override MaterialDto Build(Guid id, string code, SaveMaterialRequest r) => Fill(new MaterialDto(id, code, "", Guid.Empty, "", Guid.Empty, "", null, true, 1), code, r, 1);

    protected override MaterialDto Apply(MaterialDto d, string code, SaveMaterialRequest r) => Fill(d, code, r, d.RowVersion + 1);

    private MaterialDto Fill(MaterialDto d, string code, SaveMaterialRequest r, uint version)
    {
        var category = _categories.GetAsync(r.CategoryId).Result;
        var uom = _uoms.GetAsync(r.UomId).Result;
        return d with
        {
            Code = code, Name = r.Name.Trim(), CategoryId = category.Id, CategoryName = category.Name, UomId = uom.Id, UomCode = uom.Code,
            Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(), RowVersion = version,
        };
    }
}

// ----- Size dan Color -----

public sealed class MockSizeApi : MockCrud<SizeDto, SizeDto, SaveSizeRequest>, ISizeApi
{
    public MockSizeApi() => Seed(new SizeDto(Guid.NewGuid(), "S", "Small", 1, true, 1), new SizeDto(Guid.NewGuid(), "M", "Medium", 2, true, 1),
        new SizeDto(Guid.NewGuid(), "L", "Large", 3, true, 1), new SizeDto(Guid.NewGuid(), "XL", "Extra Large", 4, true, 1));

    protected override Guid IdOf(SizeDto d) => d.Id;
    protected override string CodeOf(SizeDto d) => d.Code;
    protected override string NameOf(SizeDto d) => d.Name;
    protected override bool ActiveOf(SizeDto d) => d.IsActive;
    protected override uint VersionOf(SizeDto d) => d.RowVersion;
    protected override SizeDto WithActive(SizeDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override SizeDto ToItem(SizeDto d) => d;
    protected override string? RequestedCode(SaveSizeRequest r) => r.Code ?? string.Empty;
    protected override string RequestedName(SaveSizeRequest r) => r.Name;
    protected override uint RequestedVersion(SaveSizeRequest r) => r.RowVersion;
    protected override SizeDto Build(Guid id, string code, SaveSizeRequest r) => new(id, code, r.Name.Trim(), r.SortOrder, true, 1);
    protected override SizeDto Apply(SizeDto d, string code, SaveSizeRequest r) => d with { Code = code, Name = r.Name.Trim(), SortOrder = r.SortOrder, RowVersion = d.RowVersion + 1 };
    protected override IEnumerable<SizeDto> Order(IEnumerable<SizeDto> rows) => rows.OrderBy(s => s.SortOrder).ThenBy(s => s.Code);

    protected override ApiException? Validate(SaveSizeRequest r) => r.SortOrder < 1 ? Invalid("sortOrder", "Urutan tampil minimal 1.") : null;
}

public sealed class MockColorApi : MockCrud<ColorDto, ColorDto, SaveColorRequest>, IColorApi
{
    public MockColorApi() => Seed(new ColorDto(Guid.NewGuid(), "NVY", "Navy", "#1F2A5A", true, 1), new ColorDto(Guid.NewGuid(), "BLK", "Hitam", "#111111", true, 1),
        new ColorDto(Guid.NewGuid(), "WHT", "Putih", null, true, 1));

    protected override Guid IdOf(ColorDto d) => d.Id;
    protected override string CodeOf(ColorDto d) => d.Code;
    protected override string NameOf(ColorDto d) => d.Name;
    protected override bool ActiveOf(ColorDto d) => d.IsActive;
    protected override uint VersionOf(ColorDto d) => d.RowVersion;
    protected override ColorDto WithActive(ColorDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override ColorDto ToItem(ColorDto d) => d;
    protected override string? RequestedCode(SaveColorRequest r) => r.Code ?? string.Empty;
    protected override string RequestedName(SaveColorRequest r) => r.Name;
    protected override uint RequestedVersion(SaveColorRequest r) => r.RowVersion;
    protected override ColorDto Build(Guid id, string code, SaveColorRequest r) => new(id, code, r.Name.Trim(), Hex(r.Hex), true, 1);
    protected override ColorDto Apply(ColorDto d, string code, SaveColorRequest r) => d with { Code = code, Name = r.Name.Trim(), Hex = Hex(r.Hex), RowVersion = d.RowVersion + 1 };

    private static string? Hex(string? hex) => string.IsNullOrWhiteSpace(hex) ? null : hex.Trim().ToUpperInvariant();

    protected override ApiException? Validate(SaveColorRequest r) =>
        string.IsNullOrWhiteSpace(r.Hex) || System.Text.RegularExpressions.Regex.IsMatch(r.Hex.Trim(), "^#[0-9a-fA-F]{6}$") ? null : Invalid("hex", "Format warna #RRGGBB.");
}

// ----- Customer, Vendor, Pekerja (kode otomatis) -----

public sealed class MockCustomerApi : MockCrud<CustomerDto, CustomerDto, SaveCustomerRequest>, ICustomerApi
{
    public MockCustomerApi() => Seed(new CustomerDto(Guid.NewGuid(), "CUS-001", "PT Maju Jaya", "Bu Ratna", "0812-555-0101", "Jl. Industri 5, Bandung", null, true, 1),
        new CustomerDto(Guid.NewGuid(), "CUS-002", "CV Sinar Garmen", null, null, null, "Pelanggan baru", true, 1));

    protected override string AutoCodePrefix => "CUS";
    protected override Guid IdOf(CustomerDto d) => d.Id;
    protected override string CodeOf(CustomerDto d) => d.Code;
    protected override string NameOf(CustomerDto d) => d.Name;
    protected override bool ActiveOf(CustomerDto d) => d.IsActive;
    protected override uint VersionOf(CustomerDto d) => d.RowVersion;
    protected override CustomerDto WithActive(CustomerDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override CustomerDto ToItem(CustomerDto d) => d;
    protected override string? RequestedCode(SaveCustomerRequest r) => null;
    protected override string RequestedName(SaveCustomerRequest r) => r.Name;
    protected override uint RequestedVersion(SaveCustomerRequest r) => r.RowVersion;
    protected override CustomerDto Build(Guid id, string code, SaveCustomerRequest r) => Apply(new CustomerDto(id, code, "", null, null, null, null, true, 0), code, r);

    protected override CustomerDto Apply(CustomerDto d, string code, SaveCustomerRequest r) =>
        d with { Code = code, Name = r.Name.Trim(), ContactPerson = N(r.ContactPerson), Phone = N(r.Phone), Address = N(r.Address), Notes = N(r.Notes), RowVersion = d.RowVersion + 1 };

    internal static string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed class MockVendorApi : MockCrud<VendorDto, VendorDto, SaveVendorRequest>, IVendorApi
{
    public MockVendorApi() => Seed(new VendorDto(Guid.NewGuid(), "VND-001", "Bordir Indah", "Bordir", "Pak Joko", "0813-555-0202", null, null, true, 1));

    protected override string AutoCodePrefix => "VND";
    protected override Guid IdOf(VendorDto d) => d.Id;
    protected override string CodeOf(VendorDto d) => d.Code;
    protected override string NameOf(VendorDto d) => d.Name;
    protected override bool ActiveOf(VendorDto d) => d.IsActive;
    protected override uint VersionOf(VendorDto d) => d.RowVersion;
    protected override VendorDto WithActive(VendorDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override VendorDto ToItem(VendorDto d) => d;
    protected override string? RequestedCode(SaveVendorRequest r) => null;
    protected override string RequestedName(SaveVendorRequest r) => r.Name;
    protected override uint RequestedVersion(SaveVendorRequest r) => r.RowVersion;
    protected override VendorDto Build(Guid id, string code, SaveVendorRequest r) => Apply(new VendorDto(id, code, "", null, null, null, null, null, true, 0), code, r);

    protected override VendorDto Apply(VendorDto d, string code, SaveVendorRequest r) => d with
    {
        Code = code, Name = r.Name.Trim(), ServiceType = MockCustomerApi.N(r.ServiceType), ContactPerson = MockCustomerApi.N(r.ContactPerson),
        Phone = MockCustomerApi.N(r.Phone), Address = MockCustomerApi.N(r.Address), Notes = MockCustomerApi.N(r.Notes), RowVersion = d.RowVersion + 1,
    };
}

public sealed class MockProcessApi : MockCrud<ProcessDto, ProcessDto, SaveProcessRequest>, IProcessApi
{
    public static readonly Guid CuttingId = Guid.Parse("00000000-0000-0000-0000-00000000b001");
    public static readonly Guid BordirId = Guid.Parse("00000000-0000-0000-0000-00000000b002");
    public static readonly Guid SewingId = Guid.Parse("00000000-0000-0000-0000-00000000b003");
    public static readonly Guid QcId = Guid.Parse("00000000-0000-0000-0000-00000000b004");

    public MockProcessApi() => Seed(
        new ProcessDto(CuttingId, "CUT", "Cutting", ProcessType.Internal, true, true, 1), new ProcessDto(BordirId, "BRD", "Bordir", ProcessType.Vendor, true, false, 1),
        new ProcessDto(SewingId, "SEW", "Sewing", ProcessType.Internal, true, true, 1), new ProcessDto(QcId, "QC", "QC", ProcessType.Internal, true, false, 1));

    protected override Guid IdOf(ProcessDto d) => d.Id;
    protected override string CodeOf(ProcessDto d) => d.Code;
    protected override string NameOf(ProcessDto d) => d.Name;
    protected override bool ActiveOf(ProcessDto d) => d.IsActive;
    protected override uint VersionOf(ProcessDto d) => d.RowVersion;
    protected override ProcessDto WithActive(ProcessDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override ProcessDto ToItem(ProcessDto d) => d;
    protected override string? RequestedCode(SaveProcessRequest r) => r.Code ?? string.Empty;
    protected override string RequestedName(SaveProcessRequest r) => r.Name;
    protected override uint RequestedVersion(SaveProcessRequest r) => r.RowVersion;
    protected override ProcessDto Build(Guid id, string code, SaveProcessRequest r) => new(id, code, r.Name.Trim(), r.Type, true, false, 1);

    protected override ProcessDto Apply(ProcessDto d, string code, SaveProcessRequest r) => d with { Code = code, Name = r.Name.Trim(), Type = r.Type, RowVersion = d.RowVersion + 1 };

    protected override ApiException? CheckDeactivate(ProcessDto d) => !d.IsUsedInRouting ? null : MockAccessStore.Problem(
        ProblemTypes.RuleViolation, 409, "Proses masih dipakai.", $"Proses {d.Name} masih ada di routing FG aktif.", ProblemCodes.MasterInUse);
}

public sealed class MockWorkerApi : MockCrud<WorkerDto, WorkerDto, SaveWorkerRequest>, IWorkerApi
{
    private readonly MockProcessApi _processes;

    public MockWorkerApi(MockProcessApi processes)
    {
        _processes = processes;
        Seed(new WorkerDto(Guid.NewGuid(), "PKJ-001", "Wati", new DateOnly(2024, 2, 5), MockProcessApi.SewingId, "Sewing", null, true, 1),
            new WorkerDto(Guid.NewGuid(), "PKJ-002", "Dedi", new DateOnly(2025, 6, 1), MockProcessApi.CuttingId, "Cutting", null, true, 1));
    }

    protected override string AutoCodePrefix => "PKJ";
    protected override Guid IdOf(WorkerDto d) => d.Id;
    protected override string CodeOf(WorkerDto d) => d.Code;
    protected override string NameOf(WorkerDto d) => d.Name;
    protected override bool ActiveOf(WorkerDto d) => d.IsActive;
    protected override uint VersionOf(WorkerDto d) => d.RowVersion;
    protected override WorkerDto WithActive(WorkerDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override WorkerDto ToItem(WorkerDto d) => d;
    protected override string? RequestedCode(SaveWorkerRequest r) => null;
    protected override string RequestedName(SaveWorkerRequest r) => r.Name;
    protected override uint RequestedVersion(SaveWorkerRequest r) => r.RowVersion;
    protected override WorkerDto Build(Guid id, string code, SaveWorkerRequest r) => Apply(new WorkerDto(id, code, "", null, null, null, null, true, 0), code, r);

    protected override WorkerDto Apply(WorkerDto d, string code, SaveWorkerRequest r)
    {
        var process = r.MainProcessId is { } id ? _processes.GetAsync(id).Result : null;
        return d with { Code = code, Name = r.Name.Trim(), JoinDate = r.JoinDate, MainProcessId = process?.Id, MainProcessName = process?.Name, Notes = MockCustomerApi.N(r.Notes), RowVersion = d.RowVersion + 1 };
    }

    protected override ApiException? Validate(SaveWorkerRequest r)
    {
        if (r.MainProcessId is { } id && !_processes.Contains(id)) { return Invalid("mainProcessId", "Proses tidak ditemukan."); }
        return null;
    }
}
