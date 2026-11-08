using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Access.Audit;

/// <summary>
/// Daftar audit log. Rentang tanggal WAJIB dan dibatasi (E11) agar kueri tidak menyapu seluruh tabel.
/// Tanggal ditafsirkan pada zona waktu aplikasi.
/// </summary>
public sealed class ListAuditLogsHandler(GmsDbContext db, AppTimeZone zone)
{
    public const int MaxRangeDays = 366;

    public async Task<PagedResult<AuditLogItem>> HandleAsync(AuditLogQuery query, CancellationToken ct)
    {
        var errors = new ErrorBag();
        if (query.From == default || query.To == default)
        {
            errors.Add("from", "Rentang tanggal wajib diisi (dari dan sampai).");
        }
        else if (query.To < query.From)
        {
            errors.Add("to", "Tanggal sampai tidak boleh sebelum tanggal dari.");
        }
        else if (query.To.DayNumber - query.From.DayNumber >= MaxRangeDays)
        {
            errors.Add("to", $"Rentang tanggal maksimal {MaxRangeDays} hari.");
        }

        errors.ThrowIfAny();

        var fromUtc = zone.StartOfDayUtc(query.From);
        var toUtcExclusive = zone.StartOfDayUtc(query.To.AddDays(1));

        var logs = db.AuditLogs.AsNoTracking()
            .Where(a => a.OccurredAt >= fromUtc && a.OccurredAt < toUtcExclusive);

        if (query.ActorId is { } actorId)
        {
            logs = logs.Where(a => a.ActorUserId == actorId);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var eventType = query.EventType.Trim();
            logs = logs.Where(a => a.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = Paging.LikePattern(query.Q.Trim());
            logs = logs.Where(a => EF.Functions.ILike(a.ActorUsername ?? "", pattern)
                                   || EF.Functions.ILike(a.TargetId ?? "", pattern)
                                   || EF.Functions.ILike(a.TargetType ?? "", pattern));
        }

        return await logs
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Select(a => new AuditLogItem(
                a.Id, a.OccurredAt, a.ActorUserId, a.ActorUsername, a.EventType, a.TargetType, a.TargetId,
                a.OldValue, a.NewValue, a.IpAddress))
            .ToPagedAsync(query.Page, query.PageSize, ct);
    }
}

public static class AuditEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/audit-logs", async (
            DateOnly? from, DateOnly? to, Guid? actorId, string? eventType, string? q, int? page, int? pageSize,
            ListAuditLogsHandler handler, CancellationToken ct) =>
        {
            var query = new AuditLogQuery
            {
                From = from ?? default, To = to ?? default, ActorId = actorId, EventType = eventType, Q = q,
                Page = page ?? 1, PageSize = pageSize ?? 25,
            };
            return Results.Ok(await handler.HandleAsync(query, ct));
        }).WithTags("Audit").RequirePermission(Permissions.AccessManagement.AuditLog.View);
    }
}
