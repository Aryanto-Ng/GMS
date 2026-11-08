using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.Master;

// ===== Pola umum =====

public record MasterListQuery : PageQuery
{
    public ActiveFilter Status { get; init; } = ActiveFilter.Active;
}

/// <summary>Daftar FG: tambahan filter "hanya yang Siap Produksi" (UI: kolom Kesiapan + filter).</summary>
public sealed record FgListQuery : MasterListQuery
{
    public bool ReadyOnly { get; init; }
}

/// <summary>Kontrak CRUD generik untuk semua master. <c>TSave</c> membawa <c>RowVersion</c> untuk pembaruan.</summary>
public interface IMasterCrudApi<TListItem, TDetail, TSave>
{
    Task<PagedResult<TListItem>> ListAsync(MasterListQuery query, CancellationToken ct = default);
    Task<TDetail> GetAsync(Guid id, CancellationToken ct = default);
    Task<TDetail> CreateAsync(TSave request, CancellationToken ct = default);
    Task<TDetail> UpdateAsync(Guid id, TSave request, CancellationToken ct = default);
    Task<TDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(string? q, CancellationToken ct = default);
}

// ===== Satuan dan Kategori Material (bentuk sederhana) =====

public sealed record SimpleMasterDto(Guid Id, string Code, string Name, bool IsActive, uint RowVersion);

public sealed record SaveSimpleMasterRequest(string Code, string Name, uint RowVersion = 0);

public interface IUomApi : IMasterCrudApi<SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>;

public interface IMaterialCategoryApi : IMasterCrudApi<SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>;

// ===== Material =====

public sealed record MaterialDto(
    Guid Id,
    string Code,
    string Name,
    Guid CategoryId,
    string CategoryName,
    Guid UomId,
    string UomCode,
    string? Description,
    bool IsActive,
    uint RowVersion);

public sealed record SaveMaterialRequest(
    string Code,
    string Name,
    Guid CategoryId,
    Guid UomId,
    string? Description,
    uint RowVersion = 0);

public interface IMaterialApi : IMasterCrudApi<MaterialDto, MaterialDto, SaveMaterialRequest>;

// ===== Size dan Color =====

public sealed record SizeDto(Guid Id, string Code, string Name, int SortOrder, bool IsActive, uint RowVersion);

public sealed record SaveSizeRequest(string Code, string Name, int SortOrder, uint RowVersion = 0);

public interface ISizeApi : IMasterCrudApi<SizeDto, SizeDto, SaveSizeRequest>;

public sealed record ColorDto(Guid Id, string Code, string Name, string? Hex, bool IsActive, uint RowVersion);

public sealed record SaveColorRequest(string Code, string Name, string? Hex, uint RowVersion = 0);

public interface IColorApi : IMasterCrudApi<ColorDto, ColorDto, SaveColorRequest>;

// ===== Customer dan Vendor =====

public sealed record CustomerDto(
    Guid Id,
    string Code,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Address,
    string? Notes,
    bool IsActive,
    uint RowVersion);

public sealed record SaveCustomerRequest(
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Address,
    string? Notes,
    uint RowVersion = 0);

public interface ICustomerApi : IMasterCrudApi<CustomerDto, CustomerDto, SaveCustomerRequest>;

public sealed record VendorDto(
    Guid Id,
    string Code,
    string Name,
    string? ServiceType,
    string? ContactPerson,
    string? Phone,
    string? Address,
    string? Notes,
    bool IsActive,
    uint RowVersion);

public sealed record SaveVendorRequest(
    string Name,
    string? ServiceType,
    string? ContactPerson,
    string? Phone,
    string? Address,
    string? Notes,
    uint RowVersion = 0);

public interface IVendorApi : IMasterCrudApi<VendorDto, VendorDto, SaveVendorRequest>;

// ===== Process =====

public sealed record ProcessDto(
    Guid Id,
    string Code,
    string Name,
    ProcessType Type,
    bool IsActive,
    bool IsUsedInRouting,
    uint RowVersion);

public sealed record SaveProcessRequest(string Code, string Name, ProcessType Type, uint RowVersion = 0);

public interface IProcessApi : IMasterCrudApi<ProcessDto, ProcessDto, SaveProcessRequest>;

// ===== Pekerja (dikelola Konveksi) =====

public sealed record WorkerDto(
    Guid Id,
    string Code,
    string Name,
    DateOnly? JoinDate,
    Guid? MainProcessId,
    string? MainProcessName,
    string? Notes,
    bool IsActive,
    uint RowVersion);

public sealed record SaveWorkerRequest(
    string Name,
    DateOnly? JoinDate,
    Guid? MainProcessId,
    string? Notes,
    uint RowVersion = 0);

public interface IWorkerApi : IMasterCrudApi<WorkerDto, WorkerDto, SaveWorkerRequest>;

// ===== FG, Size Berlaku, Routing, Tarif =====

public sealed record FgListItem(
    Guid Id,
    string Code,
    string Name,
    string? Kind,
    string? CustomerName,
    bool IsReady,
    bool IsActive);

public sealed record FgDto(
    Guid Id,
    string Code,
    string Name,
    string? Kind,
    Guid? CustomerId,
    string? CustomerName,
    string? Notes,
    bool IsActive,
    bool IsReady,
    bool IsCodeLocked,
    uint RowVersion);

public sealed record SaveFgRequest(
    string Code,
    string Name,
    string? Kind,
    Guid? CustomerId,
    string? Notes,
    uint RowVersion = 0);

public sealed record FgSizesDto(IReadOnlyList<Guid> SizeIds, uint RowVersion);

/// <summary>Menghapus size yang sudah bertarif membutuhkan <see cref="ConfirmRemoveTariffs"/> = true.</summary>
public sealed record SaveFgSizesRequest(IReadOnlyList<Guid> SizeIds, bool ConfirmRemoveTariffs, uint RowVersion);

public sealed record RoutingStepDto(int Seq, Guid ProcessId, string ProcessCode, string ProcessName, ProcessType ProcessType);

public sealed record FgRoutingDto(IReadOnlyList<RoutingStepDto> Steps, uint RowVersion);

/// <summary>ProcessIds berurutan (urutan = urutan langkah). Menghapus langkah bertarif membutuhkan konfirmasi.</summary>
public sealed record SaveRoutingRequest(IReadOnlyList<Guid> ProcessIds, bool ConfirmRemoveTariffs, uint RowVersion);

/// <summary>Satu sel tarif. <see cref="Rate"/> null = belum diisi. Presisi hingga 5 digit desimal, harus &gt; 0.</summary>
public sealed record TariffCell(Guid ProcessId, Guid SizeId, decimal? Rate);

/// <summary>Grid tarif: baris = langkah Internal (urut routing), kolom = Size Berlaku. Hanya untuk pengguna dengan MasterData.Tarif.View.</summary>
public sealed record FgTariffGridDto(
    IReadOnlyList<RoutingStepDto> InternalSteps,
    IReadOnlyList<RoutingStepDto> VendorSteps,
    IReadOnlyList<SizeRef> Sizes,
    IReadOnlyList<TariffCell> Cells,
    uint RowVersion);

public sealed record SaveTariffsRequest(IReadOnlyList<TariffCell> Cells, uint RowVersion);

public sealed record FgReadinessDto(bool IsReady, IReadOnlyList<ReadinessItem> Items);

public sealed record CopyFgRequest(Guid SourceFgId, bool ConfirmOverwrite = false);

public sealed record CopyFgPreviewDto(
    IReadOnlyList<RoutingStepDto> Steps,
    IReadOnlyList<TariffCell> Cells,
    IReadOnlyList<string> SkippedSizeCodes,
    bool WillOverwriteExisting);

public interface IFgApi : IMasterCrudApi<FgListItem, FgDto, SaveFgRequest>
{
    Task<FgSizesDto> GetSizesAsync(Guid fgId, CancellationToken ct = default);
    Task<FgSizesDto> SaveSizesAsync(Guid fgId, SaveFgSizesRequest request, CancellationToken ct = default);
    Task<FgRoutingDto> GetRoutingAsync(Guid fgId, CancellationToken ct = default);
    Task<FgRoutingDto> SaveRoutingAsync(Guid fgId, SaveRoutingRequest request, CancellationToken ct = default);
    Task<FgTariffGridDto> GetTariffsAsync(Guid fgId, CancellationToken ct = default);
    Task<FgTariffGridDto> SaveTariffsAsync(Guid fgId, SaveTariffsRequest request, CancellationToken ct = default);
    Task<FgReadinessDto> GetReadinessAsync(Guid fgId, CancellationToken ct = default);
    Task<CopyFgPreviewDto> PreviewCopyFromAsync(Guid fgId, CopyFgRequest request, CancellationToken ct = default);
    Task<FgDto> CopyFromAsync(Guid fgId, CopyFgRequest request, CancellationToken ct = default);
}

// ===== Impor CSV =====

public sealed record ImportRowResult(int RowNumber, string? Key, bool IsValid, string? Message);

public sealed record ImportResultDto(
    int TotalRows,
    int ValidRows,
    int ErrorRows,
    bool Committed,
    IReadOnlyList<ImportRowResult> Rows);

public interface IImportApi
{
    Task<FileContent> GetTemplateAsync(ImportEntity entity, CancellationToken ct = default);

    /// <summary><paramref name="dryRun"/> = true hanya memvalidasi. Semua-atau-tidak-sama-sekali.</summary>
    Task<ImportResultDto> ImportAsync(ImportEntity entity, Stream csv, string fileName, bool dryRun, CancellationToken ct = default);
}
