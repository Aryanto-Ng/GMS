using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Common;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Master;

/// <summary>Kumpulan layanan yang dibutuhkan setiap handler master (dikelompokkan agar konstruktor ringkas).</summary>
public sealed class MasterContext(
    GmsDbContext db,
    ICurrentUser user,
    IAuditWriter audit,
    TimeProvider time,
    NumberSequences sequences)
{
    public GmsDbContext Db { get; } = db;
    public ICurrentUser User { get; } = user;
    public IAuditWriter Audit { get; } = audit;
    public TimeProvider Time { get; } = time;
    public NumberSequences Sequences { get; } = sequences;
}

/// <summary>Kontrak operasi CRUD yang dipetakan generik oleh <c>MasterEndpoints</c>.</summary>
public interface IMasterCrud<TList, TDetail, TSave>
{
    Task<PagedResult<TList>> ListAsync(MasterListQuery query, CancellationToken ct);
    Task<TDetail> GetAsync(Guid id, CancellationToken ct);
    Task<TDetail> CreateAsync(TSave request, CancellationToken ct);
    Task<TDetail> UpdateAsync(Guid id, TSave request, CancellationToken ct);
    Task<TDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct);
    Task<IReadOnlyList<LookupItem>> LookupAsync(string? q, CancellationToken ct);
}

/// <summary>
/// Mesin CRUD generik untuk master (FSD Master Data bagian 3): kode unik tanpa peka huruf besar-kecil, kode terkunci setelah
/// dipakai, tidak ada penghapusan (hanya nonaktif/aktif), konkurensi optimistis, dan audit. Kelas turunan hanya menyediakan
/// aturan khusus entitas (validasi, pemetaan, pemakaian, syarat nonaktif).
/// </summary>
public abstract partial class MasterCrudHandler<TEntity, TList, TDetail, TSave>(MasterContext ctx)
    : IMasterCrud<TList, TDetail, TSave>
    where TEntity : MasterEntity, ICodedEntity, new()
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex CodePattern();

    protected MasterContext Ctx => ctx;
    protected GmsDbContext Db => ctx.Db;

    /// <summary>Nama entitas untuk pesan (mis. "Satuan").</summary>
    protected abstract string Label { get; }

    /// <summary>Bila diisi, kode dibuat otomatis (mis. "CUS" → CUS-0001) dan tidak diinput pengguna.</summary>
    protected virtual string? AutoCodePrefix => null;

    protected virtual int MaxCodeLength => 50;
    protected abstract Expression<Func<TEntity, TList>> ListSelector { get; }
    protected abstract Expression<Func<TEntity, TDetail>> DetailSelector { get; }
    protected abstract string? GetCode(TSave request);
    protected abstract uint GetRowVersion(TSave request);

    /// <summary>Validasi isian khusus entitas. <paramref name="existing"/> null = pembuatan baru.</summary>
    protected abstract Task ValidateAsync(TSave request, TEntity? existing, ErrorBag errors, CancellationToken ct);

    protected abstract void Apply(TSave request, TEntity entity);
    protected abstract object Snapshot(TEntity entity);

    /// <summary>Kode terkunci bila sudah dipakai data lain (AC-MD-03). Entitas berkode otomatis selalu terkunci.</summary>
    protected virtual Task<bool> IsCodeUsedAsync(TEntity entity, CancellationToken ct) => Task.FromResult(false);

    /// <summary>Melempar <see cref="ProblemException"/> bila penonaktifan harus ditolak (mis. masih dipakai data aktif).</summary>
    protected virtual Task CheckCanDeactivateAsync(TEntity entity, CancellationToken ct) => Task.CompletedTask;

    protected virtual IQueryable<TEntity> DefaultOrder(IQueryable<TEntity> query) => query.OrderBy(e => e.Code);

    // ===== Daftar, detail, lookup =====

    public virtual async Task<PagedResult<TList>> ListAsync(MasterListQuery query, CancellationToken ct)
    {
        var set = Db.Set<TEntity>().AsNoTracking();
        set = ApplyStatus(set, query.Status);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            set = set.Where(e => EF.Functions.ILike(e.Code, pattern) || EF.Functions.ILike(e.Name, pattern));
        }

        set = ApplySort(set, query.Sort);
        return await set.Select(ListSelector).ToPagedAsync(query.Page, query.PageSize, ct);
    }

    public virtual async Task<TDetail> GetAsync(Guid id, CancellationToken ct) =>
        await Db.Set<TEntity>().AsNoTracking().Where(e => e.Id == id).Select(DetailSelector).SingleOrDefaultAsync(ct)
        ?? throw ProblemException.NotFound(Label);

    public async Task<IReadOnlyList<LookupItem>> LookupAsync(string? q, CancellationToken ct)
    {
        var set = Db.Set<TEntity>().AsNoTracking().Where(e => e.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = Paging.LikePattern(q.Trim());
            set = set.Where(e => EF.Functions.ILike(e.Code, pattern) || EF.Functions.ILike(e.Name, pattern));
        }

        return await set.OrderBy(e => e.Code).Take(50).Select(e => new LookupItem(e.Id, e.Code, e.Name)).ToListAsync(ct);
    }

    // ===== Membuat =====

    public async Task<TDetail> CreateAsync(TSave request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        var entity = new TEntity();
        string? code = null;

        if (AutoCodePrefix is null)
        {
            code = ValidateCode(GetCode(request), errors);
        }

        await ValidateAsync(request, null, errors, ct);

        if (code is not null && !errors.HasKey("code")
                             && await Db.Set<TEntity>().AnyAsync(e => e.Code == code, ct))
        {
            throw ProblemException.Duplicate("code", "Kode sudah digunakan.");
        }

        errors.ThrowIfAny();

        await using var tx = await Db.Database.BeginTransactionAsync(ct);
        entity.Code = AutoCodePrefix is null ? code! : await Ctx.Sequences.NextCodeAsync(AutoCodePrefix, ct);
        entity.IsActive = true;
        entity.CreatedAt = Ctx.Time.GetUtcNow();
        entity.CreatedBy = Ctx.User.Id;
        Apply(request, entity);

        Db.Set<TEntity>().Add(entity);
        Ctx.Audit.Add(AuditEvents.MasterCreated, typeof(TEntity).Name, entity.Id.ToString(), null, Snapshot(entity));
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetAsync(entity.Id, ct);
    }

    // ===== Mengubah =====

    public async Task<TDetail> UpdateAsync(Guid id, TSave request, CancellationToken ct)
    {
        var entity = await Db.Set<TEntity>().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw ProblemException.NotFound(Label);
        Db.Entry(entity).Property(e => e.RowVersion).OriginalValue = GetRowVersion(request);

        var errors = new ErrorBag();
        string? newCode = null;
        if (AutoCodePrefix is null)
        {
            newCode = ValidateCode(GetCode(request), errors);
        }

        await ValidateAsync(request, entity, errors, ct);

        // Perubahan kode, termasuk hanya huruf besar-kecil, hanya diizinkan bila kode belum terkunci.
        var codeChanged = newCode is not null && !string.Equals(newCode, entity.Code, StringComparison.Ordinal);
        if (codeChanged && !errors.HasKey("code"))
        {
            if (await IsCodeUsedAsync(entity, ct))
            {
                errors.Add("code", "Kode sudah dipakai oleh data lain dan tidak dapat diubah.");
            }
            else if (await Db.Set<TEntity>().AnyAsync(e => e.Code == newCode && e.Id != id, ct))
            {
                throw ProblemException.Duplicate("code", "Kode sudah digunakan.");
            }
        }

        errors.ThrowIfAny();

        var before = Snapshot(entity);
        Apply(request, entity);
        if (codeChanged)
        {
            entity.Code = newCode!;
        }

        entity.UpdatedAt = Ctx.Time.GetUtcNow();
        entity.UpdatedBy = Ctx.User.Id;
        Ctx.Audit.Add(AuditEvents.MasterUpdated, typeof(TEntity).Name, id.ToString(), before, Snapshot(entity));
        await Db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    // ===== Nonaktif / aktif =====

    public async Task<TDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct)
    {
        var entity = await Db.Set<TEntity>().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw ProblemException.NotFound(Label);
        Db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;

        if (entity.IsActive != isActive)
        {
            if (!isActive)
            {
                await CheckCanDeactivateAsync(entity, ct);
            }

            entity.IsActive = isActive;
            entity.UpdatedAt = Ctx.Time.GetUtcNow();
            entity.UpdatedBy = Ctx.User.Id;
            Ctx.Audit.Add(isActive ? AuditEvents.MasterActivated : AuditEvents.MasterDeactivated,
                typeof(TEntity).Name, id.ToString(), null, Snapshot(entity));
            await Db.SaveChangesAsync(ct);
        }

        return await GetAsync(id, ct);
    }

    // ===== Util =====

    private string? ValidateCode(string? raw, ErrorBag errors)
    {
        var code = raw?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            errors.Add("code", "Kode wajib diisi.");
            return null;
        }

        if (code.Length > MaxCodeLength)
        {
            errors.Add("code", $"Kode maksimal {MaxCodeLength} karakter.");
            return null;
        }

        if (!CodePattern().IsMatch(code))
        {
            errors.Add("code", "Kode hanya boleh berisi huruf, angka, titik, strip, garis miring, atau garis bawah (tanpa spasi).");
            return null;
        }

        return code;
    }

    private static IQueryable<TEntity> ApplyStatus(IQueryable<TEntity> set, ActiveFilter status) => status switch
    {
        ActiveFilter.Active => set.Where(e => e.IsActive),
        ActiveFilter.Inactive => set.Where(e => !e.IsActive),
        _ => set,
    };

    private IQueryable<TEntity> ApplySort(IQueryable<TEntity> set, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return DefaultOrder(set);
        }

        var parts = sort.Split(':', 2, StringSplitOptions.TrimEntries);
        var descending = parts.Length == 2 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return parts[0].ToLowerInvariant() switch
        {
            "code" => descending ? set.OrderByDescending(e => e.Code) : set.OrderBy(e => e.Code),
            "name" => descending ? set.OrderByDescending(e => e.Name) : set.OrderBy(e => e.Name),
            _ => DefaultOrder(set),
        };
    }

    /// <summary>Penolakan nonaktif/ubah karena masih dipakai, dengan daftar pemakai sebagai penahan (maks 10).</summary>
    protected static ProblemException InUse(string message, IEnumerable<string> users, string code = ProblemCodes.MasterInUse)
    {
        var list = users.Take(10).Select(u => new RuleViolation(code, u, null, null, $"Dipakai oleh {u}.")).ToList();
        return ProblemException.Rule(code, message, list);
    }

    protected void RequireText(string? value, string path, string label, int max, ErrorBag errors, bool required = true)
    {
        var text = value?.Trim() ?? string.Empty;
        if (required && text.Length == 0)
        {
            errors.Add(path, $"{label} wajib diisi.");
        }
        else if (text.Length > max)
        {
            errors.Add(path, $"{label} maksimal {max} karakter.");
        }
    }

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
