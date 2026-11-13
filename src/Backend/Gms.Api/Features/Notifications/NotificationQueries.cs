using System.Text.Json;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Notifications;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Common;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Notifications;

/// <summary>
/// Read/write notifikasi milik pengguna yang sedang login. Setiap query dibatasi <c>recipient_user_id = saya</c> DAN jenis yang izin
/// sumbernya masih dimiliki saat ini (FSD AC-NT-13/14): notifikasi lama yang izinnya telah dicabut tidak tampil dan tidak dihitung.
/// </summary>
public sealed class NotificationQueries(GmsDbContext db, ICurrentUser user, TimeProvider time)
{
    private Guid Me => user.Id ?? throw ProblemException.Forbidden();

    private string[] AllowedTypes =>
        NotificationCatalog.All.Where(d => d.RequiredPermissions.All(user.HasPermission)).Select(d => d.TypeCode).ToArray();

    private IQueryable<Notification> Visible(string? q)
    {
        var me = Me;
        var allowed = AllowedTypes;
        IQueryable<Notification> source = db.Notifications;
        if (!string.IsNullOrWhiteSpace(q))
        {
            // params berupa jsonb: pencarian teks lewat cast, dengan karakter LIKE di-escape.
            var pattern = Paging.LikePattern(q.Trim());
            source = db.Notifications.FromSqlInterpolated($"SELECT * FROM notifications WHERE params::text ILIKE {pattern}");
        }

        return source.AsNoTracking().Where(n => n.RecipientUserId == me && allowed.Contains(n.TypeCode));
    }

    public async Task<NotificationSummary> SummaryAsync(CancellationToken ct) =>
        new(await Visible(null).CountAsync(n => n.ReadAt == null, ct));

    public async Task<PagedResult<NotificationItem>> ListAsync(NotificationListQuery query, CancellationToken ct)
    {
        var source = Visible(query.Q);
        if (query.Category is { } category)
        {
            source = source.Where(n => n.Category == category);
        }

        if (query.Unread == true)
        {
            source = source.Where(n => n.ReadAt == null);
        }

        var ordered = source.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id);
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<NotificationItem>(rows.Select(ToItem).ToList(), page, size, total);
    }

    public async Task<NotificationCounts> CountsAsync(string? q, CancellationToken ct)
    {
        var grouped = await Visible(q)
            .GroupBy(n => n.Category)
            .Select(g => new { Category = g.Key, Total = g.Count(), Unread = g.Count(n => n.ReadAt == null) })
            .ToListAsync(ct);
        var byCategory = Enum.GetValues<NotificationCategory>()
            .ToDictionary(c => c, c => grouped.FirstOrDefault(x => x.Category == c)?.Total ?? 0);
        return new NotificationCounts(grouped.Sum(x => x.Total), grouped.Sum(x => x.Unread), byCategory);
    }

    /// <summary>Milik pengguna lain, tidak ada, atau izin sudah dicabut = 404 (tidak membocorkan keberadaan).</summary>
    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var me = Me;
        var exists = await Visible(null).AnyAsync(n => n.Id == id, ct);
        if (!exists)
        {
            throw ProblemException.NotFound("Notifikasi");
        }

        var now = time.GetUtcNow();
        await db.Notifications.Where(n => n.Id == id && n.RecipientUserId == me && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    /// <summary>Seluruh notifikasi milik pemanggil (bukan hanya yang tampak di panel, AC-NT-10).</summary>
    public async Task<MarkAllReadResult> MarkAllReadAsync(CancellationToken ct)
    {
        var me = Me;
        var now = time.GetUtcNow();
        var marked = await db.Notifications.Where(n => n.RecipientUserId == me && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return new MarkAllReadResult(marked);
    }

    private static NotificationItem ToItem(Notification n)
    {
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(n.Params) ?? [];
        NotificationTarget? target = n.TargetKind is { } kind && n.TargetId is { } id ? new NotificationTarget(kind, id) : null;
        return new NotificationItem(n.Id, n.TypeCode, n.Category, parameters, target, n.CreatedAt, n.ReadAt is not null);
    }
}
