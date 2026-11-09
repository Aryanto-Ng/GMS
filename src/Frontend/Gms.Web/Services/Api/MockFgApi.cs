using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;

namespace Gms.Web.Services.Api;

/// <summary>
/// FG tiruan (ADR-014) dengan Size Berlaku, routing, tarif, kesiapan, dan salin-dari-FG. Meniru aturan API: satu rowVersion FG untuk seluruh struktur,
/// konfirmasi bila menghapus size/langkah bertarif (409 + violations), tarif &gt; 0 dengan maksimal 5 desimal, kesiapan = jumlah tarif lengkap.
/// </summary>
public sealed class MockFgApi : MockCrud<FgListItem, FgDto, SaveFgRequest>, IFgApi
{
    private readonly MockSizeApi _sizes;
    private readonly MockProcessApi _processes;
    private readonly MockCustomerApi _customers;
    private readonly Dictionary<Guid, List<Guid>> _fgSizes = [];
    private readonly Dictionary<Guid, List<Guid>> _fgRouting = [];
    private readonly Dictionary<Guid, Dictionary<(Guid Process, Guid Size), decimal>> _fgTariffs = [];

    public static readonly Guid KemejaId = Guid.Parse("00000000-0000-0000-0000-00000000f001");
    public static readonly Guid CelanaId = Guid.Parse("00000000-0000-0000-0000-00000000f002");

    public MockFgApi(MockSizeApi sizes, MockProcessApi processes, MockCustomerApi customers)
    {
        _sizes = sizes;
        _processes = processes;
        _customers = customers;
        var customer = customers.LookupAsync(null).Result[0];
        Seed(
            new FgDto(KemejaId, "KMJ-001", "Kemeja Wearpak Lengan Panjang", "Kemeja", customer.Id, customer.Name, null, true, false, false, 1),
            new FgDto(CelanaId, "CLN-002", "Celana Kerja", "Celana", null, null, "Belum disusun", true, false, false, 1));
        var sizeIds = sizes.LookupAsync(null).Result.Select(s => s.Id).ToList();
        _fgSizes[KemejaId] = sizeIds;
        _fgRouting[KemejaId] = [MockProcessApi.CuttingId, MockProcessApi.BordirId, MockProcessApi.SewingId, MockProcessApi.QcId];
        var tariffs = new Dictionary<(Guid, Guid), decimal>();
        foreach (var size in sizeIds)
        {
            tariffs[(MockProcessApi.CuttingId, size)] = 350m;
            tariffs[(MockProcessApi.SewingId, size)] = 2500m;
            tariffs[(MockProcessApi.QcId, size)] = 400m;
        }

        _fgTariffs[KemejaId] = tariffs;
    }

    protected override string AutoCodePrefix => "FG";
    protected override Guid IdOf(FgDto d) => d.Id;
    protected override string CodeOf(FgDto d) => d.Code;
    protected override string NameOf(FgDto d) => d.Name;
    protected override bool ActiveOf(FgDto d) => d.IsActive;
    protected override uint VersionOf(FgDto d) => d.RowVersion;
    protected override FgDto WithActive(FgDto d, bool active) => d with { IsActive = active, RowVersion = d.RowVersion + 1 };
    protected override string? RequestedCode(SaveFgRequest r) => r.Code ?? string.Empty;
    protected override string RequestedName(SaveFgRequest r) => r.Name;
    protected override uint RequestedVersion(SaveFgRequest r) => r.RowVersion;
    protected override FgListItem ToItem(FgDto d) => new(d.Id, d.Code, d.Name, d.Kind, d.CustomerName, Evaluate(d.Id, d.IsActive).Ready, d.IsActive);
    protected override FgDto Project(FgDto d) => d with { IsReady = Evaluate(d.Id, d.IsActive).Ready, IsCodeLocked = HasStructure(d.Id) };
    protected override bool Include(FgDto d, MasterListQuery q) => q is not FgListQuery { ReadyOnly: true } || Evaluate(d.Id, d.IsActive).Ready;

    protected override FgDto Build(Guid id, string code, SaveFgRequest r) => Apply(new FgDto(id, code, "", null, null, null, null, true, false, false, 0), code, r);

    protected override FgDto Apply(FgDto d, string code, SaveFgRequest r)
    {
        var customer = r.CustomerId is { } id ? _customers.GetAsync(id).Result : null;
        return d with
        {
            Code = HasStructure(d.Id) ? d.Code : code, Name = r.Name.Trim(), Kind = MockCustomerApi.N(r.Kind), CustomerId = customer?.Id, CustomerName = customer?.Name,
            Notes = MockCustomerApi.N(r.Notes), RowVersion = d.RowVersion + 1,
        };
    }

    protected override ApiException? Validate(SaveFgRequest r) =>
        r.CustomerId is { } id && !_customers.Contains(id) ? Invalid("customerId", "Customer tidak ditemukan.") : null;

    private bool HasStructure(Guid id) => _fgSizes.GetValueOrDefault(id)?.Count > 0 || _fgRouting.GetValueOrDefault(id)?.Count > 0;

    // ----- Struktur -----

    private List<SizeDto> SizesOf(Guid fgId) =>
        (_fgSizes.GetValueOrDefault(fgId) ?? []).Select(id => _sizes.GetAsync(id).Result).OrderBy(s => s.SortOrder).ThenBy(s => s.Code).ToList();

    private RoutingStepDto[] StepsOf(Guid fgId) =>
        (_fgRouting.GetValueOrDefault(fgId) ?? []).Select((pid, i) =>
        {
            var p = _processes.GetAsync(pid).Result;
            return new RoutingStepDto(i + 1, p.Id, p.Code, p.Name, p.Type);
        }).ToArray();

    private (bool Ready, List<ReadinessItem> Items) Evaluate(Guid fgId, bool active)
    {
        var sizes = SizesOf(fgId);
        var steps = StepsOf(fgId);
        var tariffs = _fgTariffs.GetValueOrDefault(fgId) ?? [];
        var items = new List<ReadinessItem>
        {
            new("fg.active", active, active ? "FG aktif." : "FG nonaktif.", "general"),
            new("fg.sizes", sizes.Count > 0, sizes.Count > 0 ? $"{sizes.Count} Size Berlaku: {string.Join(", ", sizes.Select(s => s.Code))}." : "Belum ada Size Berlaku.", "sizes"),
        };
        var inactive = steps.Where(s => !_processes.GetAsync(s.ProcessId).Result.IsActive).ToList();
        items.Add(steps.Length == 0 ? new ReadinessItem("fg.routing", false, "Routing belum disusun.", "routing")
            : inactive.Count > 0 ? new ReadinessItem("fg.routing", false, $"Proses nonaktif pada routing: {string.Join(", ", inactive.Select(s => s.ProcessCode))}.", "routing")
            : new ReadinessItem("fg.routing", true, $"Routing memiliki {steps.Length} langkah, semua proses aktif.", "routing"));

        var internalSteps = steps.Where(s => s.ProcessType == ProcessType.Internal).ToList();
        if (steps.Length == 0 || sizes.Count == 0)
        {
            items.Add(new ReadinessItem("fg.tariffs", false, "Tarif dievaluasi setelah routing dan Size Berlaku tersedia.", "tariffs"));
        }
        else
        {
            var missing = (from step in internalSteps from size in sizes where !tariffs.ContainsKey((step.ProcessId, size.Id)) select $"Tarif {step.ProcessName} untuk size {size.Code} belum diisi.").ToList();
            if (missing.Count == 0)
            {
                items.Add(new ReadinessItem("fg.tariffs", true, internalSteps.Count == 0 ? "Tidak ada proses Internal yang membutuhkan tarif." : $"Semua tarif terisi ({internalSteps.Count * sizes.Count} sel).", "tariffs"));
            }
            else
            {
                items.AddRange(missing.Take(50).Select(m => new ReadinessItem("fg.tariffs", false, m, "tariffs")));
            }
        }

        return (items.All(i => i.Ok), items);
    }

    private FgDto Touch(Guid fgId, uint expected)
    {
        var fg = Current(fgId);
        if (fg.RowVersion != expected) { throw Stale(); }
        return Replace(fgId, d => d with { RowVersion = d.RowVersion + 1 });
    }

    private static ApiException NeedsConfirm(string code, string title, IEnumerable<RuleViolation> violations) =>
        new(new GmsProblemDetails { Type = ProblemTypes.RuleViolation, Status = 409, Title = title, Code = code, Violations = violations.ToList(), TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    public Task<FgSizesDto> GetSizesAsync(Guid fgId, CancellationToken ct = default)
    {
        lock (Gate) { return Task.FromResult(new FgSizesDto(SizesOf(Current(fgId).Id).Select(s => s.Id).ToList(), Current(fgId).RowVersion)); }
    }

    public Task<FgSizesDto> SaveSizesAsync(Guid fgId, SaveFgSizesRequest request, CancellationToken ct = default)
    {
        lock (Gate)
        {
            var fg = Current(fgId);
            if (fg.RowVersion != request.RowVersion) { throw Stale(); }
            var ids = request.SizeIds.Distinct().ToList();
            if (ids.Count == 0) { throw Invalid("sizeIds", "Minimal 1 Size Berlaku."); }
            if (ids.Any(id => !_sizes.Contains(id))) { throw Invalid("sizeIds", "Size tidak ditemukan."); }
            var tariffs = _fgTariffs.GetValueOrDefault(fgId) ?? [];
            var removed = (_fgSizes.GetValueOrDefault(fgId) ?? []).Except(ids).ToList();
            var affected = removed.Select(id => (Size: _sizes.GetAsync(id).Result, Count: tariffs.Keys.Count(k => k.Size == id))).Where(x => x.Count > 0).ToList();
            if (affected.Count > 0 && !request.ConfirmRemoveTariffs)
            {
                throw NeedsConfirm(ProblemCodes.FgSizeHasTariffs, "Size yang dihapus sudah bertarif.",
                    affected.Select(a => new RuleViolation(ProblemCodes.FgSizeHasTariffs, $"Size {a.Size.Code}", a.Count, null, $"Tarif size {a.Size.Code} ({a.Count} proses) akan dihapus.")));
            }

            foreach (var key in tariffs.Keys.Where(k => removed.Contains(k.Size)).ToList()) { tariffs.Remove(key); }
            _fgSizes[fgId] = ids;
            var updated = Touch(fgId, request.RowVersion);
            return Task.FromResult(new FgSizesDto(SizesOf(fgId).Select(s => s.Id).ToList(), updated.RowVersion));
        }
    }

    public Task<FgRoutingDto> GetRoutingAsync(Guid fgId, CancellationToken ct = default)
    {
        lock (Gate) { return Task.FromResult(new FgRoutingDto(StepsOf(Current(fgId).Id), Current(fgId).RowVersion)); }
    }

    public Task<FgRoutingDto> SaveRoutingAsync(Guid fgId, SaveRoutingRequest request, CancellationToken ct = default)
    {
        lock (Gate)
        {
            var fg = Current(fgId);
            if (fg.RowVersion != request.RowVersion) { throw Stale(); }
            var ids = request.ProcessIds.ToList();
            if (ids.Count == 0) { throw Invalid("processIds", "Minimal 1 langkah."); }
            if (ids.Distinct().Count() != ids.Count) { throw Invalid("processIds", "Proses tidak boleh dobel pada routing."); }
            if (ids.Any(id => !_processes.Contains(id))) { throw Invalid("processIds", "Proses tidak ditemukan."); }
            var tariffs = _fgTariffs.GetValueOrDefault(fgId) ?? [];
            var removed = (_fgRouting.GetValueOrDefault(fgId) ?? []).Except(ids).ToList();
            var affected = removed.Select(id => (Process: _processes.GetAsync(id).Result, Count: tariffs.Keys.Count(k => k.Process == id))).Where(x => x.Count > 0).ToList();
            if (affected.Count > 0 && !request.ConfirmRemoveTariffs)
            {
                throw NeedsConfirm(ProblemCodes.FgStepHasTariffs, "Langkah yang dihapus sudah bertarif.",
                    affected.Select(a => new RuleViolation(ProblemCodes.FgStepHasTariffs, $"Proses {a.Process.Code}", a.Count, null, $"Tarif langkah {a.Process.Name} ({a.Count} size) akan dihapus.")));
            }

            foreach (var key in tariffs.Keys.Where(k => removed.Contains(k.Process)).ToList()) { tariffs.Remove(key); }
            _fgRouting[fgId] = ids;
            var updated = Touch(fgId, request.RowVersion);
            return Task.FromResult(new FgRoutingDto(StepsOf(fgId), updated.RowVersion));
        }
    }

    private FgTariffGridDto Grid(Guid fgId)
    {
        var steps = StepsOf(fgId);
        var tariffs = _fgTariffs.GetValueOrDefault(fgId) ?? [];
        var sizes = SizesOf(fgId);
        var internalSteps = steps.Where(s => s.ProcessType == ProcessType.Internal).ToList();
        var cells = internalSteps.SelectMany(s => sizes.Select(z => new TariffCell(s.ProcessId, z.Id, tariffs.TryGetValue((s.ProcessId, z.Id), out var rate) ? rate : null))).ToList();
        return new FgTariffGridDto(internalSteps, steps.Where(s => s.ProcessType == ProcessType.Vendor).ToList(),
            sizes.Select(z => new SizeRef(z.Id, z.Code, z.Name, z.SortOrder)).ToList(), cells, Current(fgId).RowVersion);
    }

    public Task<FgTariffGridDto> GetTariffsAsync(Guid fgId, CancellationToken ct = default)
    {
        lock (Gate) { Current(fgId); return Task.FromResult(Grid(fgId)); }
    }

    public Task<FgTariffGridDto> SaveTariffsAsync(Guid fgId, SaveTariffsRequest request, CancellationToken ct = default)
    {
        lock (Gate)
        {
            var fg = Current(fgId);
            if (fg.RowVersion != request.RowVersion) { throw Stale(); }
            var internalIds = StepsOf(fgId).Where(s => s.ProcessType == ProcessType.Internal).Select(s => s.ProcessId).ToHashSet();
            var sizeIds = SizesOf(fgId).Select(s => s.Id).ToHashSet();
            var errors = new Dictionary<string, string[]>();
            var cells = request.Cells ?? [];
            for (var i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (!internalIds.Contains(c.ProcessId)) { errors[$"cells[{i}].processId"] = ["Tarif hanya untuk langkah bertipe Internal pada routing FG ini."]; continue; }
                if (!sizeIds.Contains(c.SizeId)) { errors[$"cells[{i}].sizeId"] = ["Size bukan Size Berlaku FG ini."]; continue; }
                if (c.Rate is { } rate)
                {
                    if (rate <= 0) { errors[$"cells[{i}].rate"] = ["Tarif harus lebih dari 0."]; }
                    else if (decimal.Round(rate, 5) != rate) { errors[$"cells[{i}].rate"] = ["Tarif maksimal 5 digit di belakang koma."]; }
                    else if (rate > 999_999_999m) { errors[$"cells[{i}].rate"] = ["Tarif terlalu besar."]; }
                }
            }

            if (errors.Count > 0)
            {
                throw MockAccessStore.Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: errors);
            }

            if (!_fgTariffs.TryGetValue(fgId, out var tariffs)) { _fgTariffs[fgId] = tariffs = []; }
            foreach (var c in cells)
            {
                if (c.Rate is { } r) { tariffs[(c.ProcessId, c.SizeId)] = r; } else { tariffs.Remove((c.ProcessId, c.SizeId)); }
            }

            Touch(fgId, request.RowVersion);
            return Task.FromResult(Grid(fgId));
        }
    }

    public Task<FgReadinessDto> GetReadinessAsync(Guid fgId, CancellationToken ct = default)
    {
        lock (Gate)
        {
            var fg = Current(fgId);
            var (ready, items) = Evaluate(fgId, fg.IsActive);
            return Task.FromResult(new FgReadinessDto(ready, items));
        }
    }

    private CopyFgPreviewDto Preview(Guid fgId, CopyFgRequest request)
    {
        Current(fgId);
        if (request.SourceFgId == fgId) { throw Invalid("sourceFgId", "FG sumber harus berbeda dengan FG tujuan."); }
        if (!Contains(request.SourceFgId)) { throw Invalid("sourceFgId", "FG sumber tidak ditemukan."); }
        var steps = StepsOf(request.SourceFgId);
        if (steps.Length == 0) { throw Invalid("sourceFgId", "FG sumber belum memiliki routing."); }
        var inactive = steps.Where(s => !_processes.GetAsync(s.ProcessId).Result.IsActive).Select(s => s.ProcessCode).ToList();
        if (inactive.Count > 0) { throw Invalid("sourceFgId", $"Routing sumber memuat proses nonaktif: {string.Join(", ", inactive)}."); }
        var target = (_fgSizes.GetValueOrDefault(fgId) ?? []).ToHashSet();
        var source = _fgTariffs.GetValueOrDefault(request.SourceFgId) ?? [];
        var cells = source.Where(t => target.Contains(t.Key.Size)).Select(t => new TariffCell(t.Key.Process, t.Key.Size, t.Value)).ToList();
        var skipped = source.Where(t => !target.Contains(t.Key.Size)).Select(t => _sizes.GetAsync(t.Key.Size).Result.Code).Distinct().Order().ToList();
        var overwrite = (_fgRouting.GetValueOrDefault(fgId)?.Count ?? 0) > 0 || (_fgTariffs.GetValueOrDefault(fgId)?.Count ?? 0) > 0;
        return new CopyFgPreviewDto(steps, cells, skipped, overwrite);
    }

    public Task<CopyFgPreviewDto> PreviewCopyFromAsync(Guid fgId, CopyFgRequest request, CancellationToken ct = default)
    {
        lock (Gate) { return Task.FromResult(Preview(fgId, request)); }
    }

    public Task<FgDto> CopyFromAsync(Guid fgId, CopyFgRequest request, CancellationToken ct = default)
    {
        lock (Gate)
        {
            var preview = Preview(fgId, request);
            if (preview.WillOverwriteExisting && !request.ConfirmOverwrite)
            {
                throw MockAccessStore.Problem(ProblemTypes.RuleViolation, 409, "FG tujuan sudah memiliki routing/tarif. Konfirmasi untuk menimpanya.", code: ProblemCodes.FgCopyNeedsConfirm);
            }

            _fgRouting[fgId] = preview.Steps.Select(s => s.ProcessId).ToList();
            _fgTariffs[fgId] = preview.Cells.Where(c => c.Rate is not null).ToDictionary(c => (c.ProcessId, c.SizeId), c => c.Rate!.Value);
            Replace(fgId, d => d with { RowVersion = d.RowVersion + 1 });
            return Task.FromResult(Project(Current(fgId)));
        }
    }
}
