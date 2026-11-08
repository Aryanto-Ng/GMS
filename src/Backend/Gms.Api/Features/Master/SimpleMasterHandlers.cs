using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Gms.Api.Common;
using Gms.Domain.Master;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Master;

// ===== Satuan & Kategori Material =====

public sealed class UomHandler(MasterContext ctx)
    : MasterCrudHandler<Uom, SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>(ctx)
{
    protected override string Label => "Satuan";
    protected override int MaxCodeLength => 20;
    protected override Expression<Func<Uom, SimpleMasterDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Uom, SimpleMasterDto>> DetailSelector =>
        e => new SimpleMasterDto(e.Id, e.Code, e.Name, e.IsActive, e.RowVersion);

    protected override string? GetCode(SaveSimpleMasterRequest request) => request.Code;
    protected override uint GetRowVersion(SaveSimpleMasterRequest request) => request.RowVersion;

    protected override Task ValidateAsync(SaveSimpleMasterRequest request, Uom? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 100, errors);
        return Task.CompletedTask;
    }

    protected override void Apply(SaveSimpleMasterRequest request, Uom entity) => entity.Name = request.Name.Trim();
    protected override object Snapshot(Uom entity) => new { entity.Code, entity.Name, entity.IsActive };

    protected override Task<bool> IsCodeUsedAsync(Uom entity, CancellationToken ct) =>
        Db.Materials.AnyAsync(m => m.UomId == entity.Id, ct);

    protected override async Task CheckCanDeactivateAsync(Uom entity, CancellationToken ct)
    {
        var users = await Db.Materials.Where(m => m.UomId == entity.Id && m.IsActive).Select(m => m.Code).Take(10).ToListAsync(ct);
        if (users.Count > 0)
        {
            throw InUse("Satuan masih dipakai oleh material aktif.", users.Select(c => "Material " + c));
        }
    }
}

public sealed class MaterialCategoryHandler(MasterContext ctx)
    : MasterCrudHandler<MaterialCategory, SimpleMasterDto, SimpleMasterDto, SaveSimpleMasterRequest>(ctx)
{
    protected override string Label => "Kategori material";
    protected override int MaxCodeLength => 30;
    protected override Expression<Func<MaterialCategory, SimpleMasterDto>> ListSelector => DetailSelector;
    protected override Expression<Func<MaterialCategory, SimpleMasterDto>> DetailSelector =>
        e => new SimpleMasterDto(e.Id, e.Code, e.Name, e.IsActive, e.RowVersion);

    protected override string? GetCode(SaveSimpleMasterRequest request) => request.Code;
    protected override uint GetRowVersion(SaveSimpleMasterRequest request) => request.RowVersion;

    protected override Task ValidateAsync(SaveSimpleMasterRequest request, MaterialCategory? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 100, errors);
        return Task.CompletedTask;
    }

    protected override void Apply(SaveSimpleMasterRequest request, MaterialCategory entity) => entity.Name = request.Name.Trim();
    protected override object Snapshot(MaterialCategory entity) => new { entity.Code, entity.Name, entity.IsActive };

    protected override Task<bool> IsCodeUsedAsync(MaterialCategory entity, CancellationToken ct) =>
        Db.Materials.AnyAsync(m => m.CategoryId == entity.Id, ct);

    protected override async Task CheckCanDeactivateAsync(MaterialCategory entity, CancellationToken ct)
    {
        var users = await Db.Materials.Where(m => m.CategoryId == entity.Id && m.IsActive).Select(m => m.Code).Take(10).ToListAsync(ct);
        if (users.Count > 0)
        {
            throw InUse("Kategori masih dipakai oleh material aktif.", users.Select(c => "Material " + c));
        }
    }
}

// ===== Material =====

public sealed class MaterialHandler(MasterContext ctx)
    : MasterCrudHandler<Material, MaterialDto, MaterialDto, SaveMaterialRequest>(ctx)
{
    protected override string Label => "Material";
    protected override Expression<Func<Material, MaterialDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Material, MaterialDto>> DetailSelector =>
        e => new MaterialDto(
            e.Id, e.Code, e.Name,
            e.CategoryId, Db.MaterialCategories.Where(c => c.Id == e.CategoryId).Select(c => c.Name).First(),
            e.UomId, Db.Uoms.Where(u => u.Id == e.UomId).Select(u => u.Code).First(),
            e.Description, e.IsActive, e.RowVersion);

    protected override string? GetCode(SaveMaterialRequest request) => request.Code;
    protected override uint GetRowVersion(SaveMaterialRequest request) => request.RowVersion;

    protected override async Task ValidateAsync(SaveMaterialRequest request, Material? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 200, errors);
        RequireText(request.Description, "description", "Spesifikasi", 1000, errors, required: false);

        if (existing is null || existing.CategoryId != request.CategoryId)
        {
            if (!await Db.MaterialCategories.AnyAsync(c => c.Id == request.CategoryId && c.IsActive, ct))
            {
                errors.Add("categoryId", "Kategori tidak ditemukan atau nonaktif.");
            }
        }

        if (existing is null || existing.UomId != request.UomId)
        {
            if (!await Db.Uoms.AnyAsync(u => u.Id == request.UomId && u.IsActive, ct))
            {
                errors.Add("uomId", "Satuan tidak ditemukan atau nonaktif.");
            }
        }
    }

    protected override void Apply(SaveMaterialRequest request, Material entity)
    {
        entity.Name = request.Name.Trim();
        entity.CategoryId = request.CategoryId;
        entity.UomId = request.UomId;
        entity.Description = Clean(request.Description);
    }

    protected override object Snapshot(Material entity) =>
        new { entity.Code, entity.Name, entity.CategoryId, entity.UomId, entity.Description, entity.IsActive };

    protected override Task<bool> IsCodeUsedAsync(Material entity, CancellationToken ct) =>
        Db.MaterialHandoverLines.AnyAsync(l => l.MaterialId == entity.Id, ct);
}

// ===== Size & Color =====

public sealed class SizeHandler(MasterContext ctx) : MasterCrudHandler<Size, SizeDto, SizeDto, SaveSizeRequest>(ctx)
{
    protected override string Label => "Size";
    protected override int MaxCodeLength => 20;
    protected override Expression<Func<Size, SizeDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Size, SizeDto>> DetailSelector =>
        e => new SizeDto(e.Id, e.Code, e.Name, e.SortOrder, e.IsActive, e.RowVersion);

    protected override IQueryable<Size> DefaultOrder(IQueryable<Size> query) => query.OrderBy(e => e.SortOrder).ThenBy(e => e.Code);
    protected override string? GetCode(SaveSizeRequest request) => request.Code;
    protected override uint GetRowVersion(SaveSizeRequest request) => request.RowVersion;

    protected override Task ValidateAsync(SaveSizeRequest request, Size? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 50, errors);
        if (request.SortOrder < 0)
        {
            errors.Add("sortOrder", "Urutan tampil tidak boleh negatif.");
        }

        return Task.CompletedTask;
    }

    protected override void Apply(SaveSizeRequest request, Size entity)
    {
        entity.Name = request.Name.Trim();
        entity.SortOrder = request.SortOrder;
    }

    protected override object Snapshot(Size entity) => new { entity.Code, entity.Name, entity.SortOrder, entity.IsActive };

    protected override async Task<bool> IsCodeUsedAsync(Size entity, CancellationToken ct) =>
        await Db.FgSizes.AnyAsync(s => s.SizeId == entity.Id, ct)
        || await Db.PoLines.AnyAsync(l => l.SizeId == entity.Id, ct)
        || await Db.StockBalances.AnyAsync(s => s.SizeId == entity.Id, ct);

    protected override async Task CheckCanDeactivateAsync(Size entity, CancellationToken ct)
    {
        var users = await Db.FgSizes
            .Where(s => s.SizeId == entity.Id)
            .Join(Db.Fgs.Where(f => f.IsActive), s => s.FgId, f => f.Id, (s, f) => f.Code)
            .Take(10).ToListAsync(ct);
        if (users.Count > 0)
        {
            throw InUse("Size masih menjadi Size Berlaku pada FG aktif.", users.Select(c => "FG " + c));
        }
    }
}

public sealed partial class ColorHandler(MasterContext ctx) : MasterCrudHandler<Color, ColorDto, ColorDto, SaveColorRequest>(ctx)
{
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexPattern();

    protected override string Label => "Color";
    protected override int MaxCodeLength => 30;
    protected override Expression<Func<Color, ColorDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Color, ColorDto>> DetailSelector =>
        e => new ColorDto(e.Id, e.Code, e.Name, e.Hex, e.IsActive, e.RowVersion);

    protected override string? GetCode(SaveColorRequest request) => request.Code;
    protected override uint GetRowVersion(SaveColorRequest request) => request.RowVersion;

    protected override Task ValidateAsync(SaveColorRequest request, Color? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 100, errors);
        if (!string.IsNullOrWhiteSpace(request.Hex) && !HexPattern().IsMatch(request.Hex.Trim()))
        {
            errors.Add("hex", "Kode warna harus berformat #RRGGBB.");
        }

        return Task.CompletedTask;
    }

    protected override void Apply(SaveColorRequest request, Color entity)
    {
        entity.Name = request.Name.Trim();
        entity.Hex = Clean(request.Hex)?.ToUpperInvariant();
    }

    protected override object Snapshot(Color entity) => new { entity.Code, entity.Name, entity.Hex, entity.IsActive };

    protected override async Task<bool> IsCodeUsedAsync(Color entity, CancellationToken ct) =>
        await Db.PoLines.AnyAsync(l => l.ColorId == entity.Id, ct)
        || await Db.StockBalances.AnyAsync(s => s.ColorId == entity.Id, ct);
}

// ===== Customer & Vendor (kode otomatis) =====

public sealed class CustomerHandler(MasterContext ctx)
    : MasterCrudHandler<Customer, CustomerDto, CustomerDto, SaveCustomerRequest>(ctx)
{
    protected override string Label => "Customer";
    protected override string? AutoCodePrefix => "CUS";
    protected override Expression<Func<Customer, CustomerDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Customer, CustomerDto>> DetailSelector =>
        e => new CustomerDto(e.Id, e.Code, e.Name, e.ContactPerson, e.Phone, e.Address, e.Notes, e.IsActive, e.RowVersion);

    protected override string? GetCode(SaveCustomerRequest request) => null;
    protected override uint GetRowVersion(SaveCustomerRequest request) => request.RowVersion;

    protected override async Task ValidateAsync(SaveCustomerRequest request, Customer? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 200, errors);
        RequireText(request.ContactPerson, "contactPerson", "PIC", 150, errors, required: false);
        RequireText(request.Phone, "phone", "Telepon", 50, errors, required: false);
        RequireText(request.Address, "address", "Alamat", 500, errors, required: false);
        RequireText(request.Notes, "notes", "Keterangan", 1000, errors, required: false);

        var name = request.Name?.Trim();
        if (!errors.HasKey("name") && !string.IsNullOrEmpty(name)
            && await Db.Customers.AnyAsync(c => c.Name == name && (existing == null || c.Id != existing.Id), ct))
        {
            throw ProblemException.Duplicate("name", "Nama customer sudah digunakan.");
        }
    }

    protected override void Apply(SaveCustomerRequest request, Customer entity)
    {
        entity.Name = request.Name.Trim();
        entity.ContactPerson = Clean(request.ContactPerson);
        entity.Phone = Clean(request.Phone);
        entity.Address = Clean(request.Address);
        entity.Notes = Clean(request.Notes);
    }

    protected override object Snapshot(Customer entity) =>
        new { entity.Code, entity.Name, entity.ContactPerson, entity.Phone, entity.Address, entity.IsActive };
}

public sealed class VendorHandler(MasterContext ctx)
    : MasterCrudHandler<Vendor, VendorDto, VendorDto, SaveVendorRequest>(ctx)
{
    protected override string Label => "Vendor";
    protected override string? AutoCodePrefix => "VND";
    protected override Expression<Func<Vendor, VendorDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Vendor, VendorDto>> DetailSelector =>
        e => new VendorDto(e.Id, e.Code, e.Name, e.ServiceType, e.ContactPerson, e.Phone, e.Address, e.Notes, e.IsActive, e.RowVersion);

    protected override string? GetCode(SaveVendorRequest request) => null;
    protected override uint GetRowVersion(SaveVendorRequest request) => request.RowVersion;

    protected override Task ValidateAsync(SaveVendorRequest request, Vendor? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 200, errors);
        RequireText(request.ServiceType, "serviceType", "Jenis layanan", 100, errors, required: false);
        RequireText(request.ContactPerson, "contactPerson", "PIC", 150, errors, required: false);
        RequireText(request.Phone, "phone", "Telepon", 50, errors, required: false);
        RequireText(request.Address, "address", "Alamat", 500, errors, required: false);
        RequireText(request.Notes, "notes", "Keterangan", 1000, errors, required: false);
        return Task.CompletedTask;
    }

    protected override void Apply(SaveVendorRequest request, Vendor entity)
    {
        entity.Name = request.Name.Trim();
        entity.ServiceType = Clean(request.ServiceType);
        entity.ContactPerson = Clean(request.ContactPerson);
        entity.Phone = Clean(request.Phone);
        entity.Address = Clean(request.Address);
        entity.Notes = Clean(request.Notes);
    }

    protected override object Snapshot(Vendor entity) =>
        new { entity.Code, entity.Name, entity.ServiceType, entity.ContactPerson, entity.Phone, entity.IsActive };
}

// ===== Process =====

public sealed class ProcessHandler(MasterContext ctx)
    : MasterCrudHandler<Process, ProcessDto, ProcessDto, SaveProcessRequest>(ctx)
{
    protected override string Label => "Proses";
    protected override int MaxCodeLength => 30;
    protected override Expression<Func<Process, ProcessDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Process, ProcessDto>> DetailSelector =>
        e => new ProcessDto(e.Id, e.Code, e.Name, e.Type, e.IsActive, Db.FgRoutingSteps.Any(s => s.ProcessId == e.Id), e.RowVersion);

    protected override string? GetCode(SaveProcessRequest request) => request.Code;
    protected override uint GetRowVersion(SaveProcessRequest request) => request.RowVersion;

    protected override async Task ValidateAsync(SaveProcessRequest request, Process? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 100, errors);
        if (!Enum.IsDefined(request.Type))
        {
            errors.Add("type", "Tipe proses tidak valid.");
        }
        else if (existing is not null && existing.Type != request.Type && await IsInRoutingAsync(existing, ct))
        {
            // AC-MD-13: tipe tidak dapat diubah setelah dipakai pada Routing.
            throw ProblemException.Rule(ProblemCodes.ProcessTypeLocked,
                "Tipe proses tidak dapat diubah karena sudah dipakai pada routing.");
        }
    }

    protected override void Apply(SaveProcessRequest request, Process entity)
    {
        entity.Name = request.Name.Trim();
        entity.Type = request.Type;
    }

    protected override object Snapshot(Process entity) => new { entity.Code, entity.Name, type = entity.Type.ToString(), entity.IsActive };

    protected override async Task<bool> IsCodeUsedAsync(Process entity, CancellationToken ct) =>
        await IsInRoutingAsync(entity, ct) || await Db.Workers.AnyAsync(w => w.MainProcessId == entity.Id, ct);

    private async Task<bool> IsInRoutingAsync(Process entity, CancellationToken ct) =>
        await Db.FgRoutingSteps.AnyAsync(s => s.ProcessId == entity.Id, ct)
        || await Db.WoRoutingSnapshots.AnyAsync(s => s.ProcessId == entity.Id, ct);

    /// <summary>AC-MD-14: ditolak bila masih ada di Routing FG aktif.</summary>
    protected override async Task CheckCanDeactivateAsync(Process entity, CancellationToken ct)
    {
        var users = await Db.FgRoutingSteps
            .Where(s => s.ProcessId == entity.Id)
            .Join(Db.Fgs.Where(f => f.IsActive), s => s.FgId, f => f.Id, (s, f) => f.Code)
            .Take(10).ToListAsync(ct);
        if (users.Count > 0)
        {
            throw InUse("Proses masih ada di routing FG aktif. Hapus dari routing terlebih dahulu.", users.Select(c => "FG " + c));
        }
    }
}

// ===== Pekerja (dikelola Konveksi) =====

public sealed class WorkerHandler(MasterContext ctx)
    : MasterCrudHandler<Worker, WorkerDto, WorkerDto, SaveWorkerRequest>(ctx)
{
    protected override string Label => "Pekerja";
    protected override string? AutoCodePrefix => "PKJ";
    protected override Expression<Func<Worker, WorkerDto>> ListSelector => DetailSelector;
    protected override Expression<Func<Worker, WorkerDto>> DetailSelector =>
        e => new WorkerDto(
            e.Id, e.Code, e.Name, e.JoinDate, e.MainProcessId,
            Db.Processes.Where(p => p.Id == e.MainProcessId).Select(p => p.Name).FirstOrDefault(),
            e.Notes, e.IsActive, e.RowVersion);

    protected override string? GetCode(SaveWorkerRequest request) => null;
    protected override uint GetRowVersion(SaveWorkerRequest request) => request.RowVersion;

    protected override async Task ValidateAsync(SaveWorkerRequest request, Worker? existing, ErrorBag errors, CancellationToken ct)
    {
        RequireText(request.Name, "name", "Nama", 150, errors);
        RequireText(request.Notes, "notes", "Keterangan", 1000, errors, required: false);

        if (request.MainProcessId is { } processId && existing?.MainProcessId != processId
            && !await Db.Processes.AnyAsync(p => p.Id == processId && p.IsActive && p.Type == ProcessType.Internal, ct))
        {
            errors.Add("mainProcessId", "Proses utama harus proses Internal yang aktif.");
        }
    }

    protected override void Apply(SaveWorkerRequest request, Worker entity)
    {
        entity.Name = request.Name.Trim();
        entity.JoinDate = request.JoinDate;
        entity.MainProcessId = request.MainProcessId;
        entity.Notes = Clean(request.Notes);
    }

    protected override object Snapshot(Worker entity) =>
        new { entity.Code, entity.Name, entity.JoinDate, entity.MainProcessId, entity.IsActive };

    // Pekerja nonaktif tetap muncul pada data lama; menonaktifkan selalu diizinkan (FSD E13).
}
