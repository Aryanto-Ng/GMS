using Gms.Shared.Common;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Common;

public static class Paging
{
    public const int MaxPageSize = 200;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, MaxPageSize));

    /// <summary>Menjalankan hitung total + halaman pada query yang sudah difilter dan diurutkan.</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize, CancellationToken ct)
    {
        var (p, size) = Normalize(page, pageSize);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<T>(items, p, size, total);
    }

    /// <summary>Meloloskan karakter khusus pola LIKE agar pencarian teks bebas aman.</summary>
    public static string LikePattern(string text) =>
        "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
