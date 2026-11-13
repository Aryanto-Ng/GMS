using Gms.Api.Security;
using Gms.Shared.Notifications;
using Gms.Shared.Security;

namespace Gms.Api.Features.Notifications;

public static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifikasi");

        // Pengguna sendiri: cukup login (FSD AC-NT-20). Semua query dibatasi ke milik pemanggil di NotificationQueries.
        group.MapGet("/summary", async (NotificationQueries q, CancellationToken ct) => Results.Ok(await q.SummaryAsync(ct))).RequireLogin();
        group.MapGet("/counts", async (string? q, NotificationQueries queries, CancellationToken ct) => Results.Ok(await queries.CountsAsync(q, ct))).RequireLogin();
        group.MapGet("", async (
            int? page, int? pageSize, string? q, string? sort, NotificationCategory? category, bool? unread,
            NotificationQueries queries, CancellationToken ct) =>
        {
            var query = new NotificationListQuery { Page = page ?? 1, PageSize = pageSize ?? 25, Q = q, Sort = sort, Category = category, Unread = unread };
            return Results.Ok(await queries.ListAsync(query, ct));
        }).RequireLogin();
        group.MapPost("/{id:guid}/read", async (Guid id, NotificationQueries q, CancellationToken ct) =>
        {
            await q.MarkReadAsync(id, ct);
            return Results.NoContent();
        }).RequireLogin();
        group.MapPost("/read-all", async (NotificationQueries q, CancellationToken ct) => Results.Ok(await q.MarkAllReadAsync(ct))).RequireLogin();

        // Pengaturan (Administrator secara bawaan).
        group.MapGet("/settings", async (GetNotificationSettingsHandler h, CancellationToken ct) => Results.Ok(await h.HandleAsync(ct)))
            .RequirePermission(Permissions.Notifikasi.Pengaturan.View);
        group.MapPut("/settings", async (UpdateNotificationSettingsRequest request, UpdateNotificationSettingsHandler h, CancellationToken ct) =>
            Results.Ok(await h.HandleAsync(request, ct)))
            .RequirePermission(Permissions.Notifikasi.Pengaturan.Edit);
    }
}
