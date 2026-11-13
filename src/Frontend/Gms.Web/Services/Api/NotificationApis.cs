using Gms.Shared.Common;
using Gms.Shared.Notifications;
using Gms.Shared.Problems;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Klien notifikasi pengguna ke API sungguhan (<c>/api/v1/notifications</c>, ApiConventions 4.12).</summary>
public sealed class HttpNotificationsApi(GmsApiClient client) : INotificationsApi
{
    public Task<NotificationSummary> GetSummaryAsync(CancellationToken ct = default) =>
        client.GetAsync<NotificationSummary>("/api/v1/notifications/summary", ct);

    public Task<PagedResult<NotificationItem>> ListAsync(NotificationListQuery query, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<NotificationItem>>("/api/v1/notifications" + ApiQuery.Build(
            ("page", query.Page), ("pageSize", query.PageSize), ("q", query.Q), ("sort", query.Sort), ("category", query.Category), ("unread", query.Unread)), ct);

    public Task<NotificationCounts> GetCountsAsync(string? q = null, CancellationToken ct = default) =>
        client.GetAsync<NotificationCounts>("/api/v1/notifications/counts" + ApiQuery.Build(("q", q)), ct);

    public Task MarkReadAsync(Guid id, CancellationToken ct = default) => client.PostAsync($"/api/v1/notifications/{id}/read", null, ct);

    public Task<MarkAllReadResult> MarkAllReadAsync(CancellationToken ct = default) =>
        client.PostAsync<MarkAllReadResult>("/api/v1/notifications/read-all", null, ct);
}

/// <summary>Klien pengaturan notifikasi (Administrator) ke API sungguhan.</summary>
public sealed class HttpNotificationSettingsApi(GmsApiClient client) : INotificationSettingsApi
{
    public Task<NotificationSettingsDto> GetAsync(CancellationToken ct = default) =>
        client.GetAsync<NotificationSettingsDto>("/api/v1/notifications/settings", ct);

    public Task<NotificationSettingsDto> UpdateAsync(UpdateNotificationSettingsRequest request, CancellationToken ct = default) =>
        client.PutAsync<NotificationSettingsDto>("/api/v1/notifications/settings", request, ct);
}

/// <summary>
/// Mock notifikasi (ADR-014): state bertahan selama proses (singleton). Meniru perilaku API: hanya milik sendiri, filter kategori/belum dibaca/pencarian,
/// 404 untuk id yang tidak dikenal (<see cref="ApiException"/> dengan Problem Details).
/// </summary>
public sealed class MockNotificationsApi : INotificationsApi
{
    private readonly List<NotificationItem> _items;
    private readonly object _lock = new();

    public MockNotificationsApi()
    {
        var now = DateTimeOffset.UtcNow;
        _items =
        [
            Make("NC-01", NotificationCategory.Penjualan, new() { ["code"] = "PO-202611-0001", ["lines"] = "2" }, NotificationTargetKind.PurchaseOrder, now.AddMinutes(-5), false),
            Make("NE-01", NotificationCategory.Produksi, new() { ["code"] = "WO-202611-0004", ["fg"] = "KMJ-001", ["customer"] = "PT Maju" }, NotificationTargetKind.WorkOrder, now.AddHours(-2), false),
            Make("NC-04", NotificationCategory.Penggajian, new() { ["code"] = "GJ-20261102-20261108", ["endDate"] = "2026-11-08" }, NotificationTargetKind.PayrollPeriod, now.AddDays(-1), false),
            Make("NE-11", NotificationCategory.Gudang, new() { ["fg"] = "KMJ-001", ["color"] = "Navy", ["size"] = "M" }, NotificationTargetKind.StockLedger, now.AddDays(-2), true),
            Make("NE-09", NotificationCategory.Keamanan, new() { ["username"] = "siti", ["attempts"] = "5" }, NotificationTargetKind.UserList, now.AddDays(-2), true),
        ];
    }

    private static NotificationItem Make(string type, NotificationCategory category, Dictionary<string, string> p, NotificationTargetKind kind, DateTimeOffset at, bool read) =>
        new(Guid.NewGuid(), type, category, p, new NotificationTarget(kind, Guid.NewGuid().ToString()), at, read);

    public Task<NotificationSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(new NotificationSummary(_items.Count(i => !i.IsRead)));
        }
    }

    public Task<PagedResult<NotificationItem>> ListAsync(NotificationListQuery query, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var rows = Filter(query.Q).Where(i => query.Category is null || i.Category == query.Category).Where(i => query.Unread != true || !i.IsRead)
                .OrderByDescending(i => i.CreatedAt).ToList();
            var page = Math.Max(query.Page, 1);
            var size = query.PageSize <= 0 ? 25 : query.PageSize;
            return Task.FromResult(new PagedResult<NotificationItem>(rows.Skip((page - 1) * size).Take(size).ToList(), page, size, rows.Count));
        }
    }

    public Task<NotificationCounts> GetCountsAsync(string? q = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var rows = Filter(q).ToList();
            var by = Enum.GetValues<NotificationCategory>().ToDictionary(c => c, c => rows.Count(r => r.Category == c));
            return Task.FromResult(new NotificationCounts(rows.Count, rows.Count(r => !r.IsRead), by));
        }
    }

    public Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index < 0)
            {
                throw new ApiException(new Gms.Shared.Problems.GmsProblemDetails { Type = Gms.Shared.Problems.ProblemTypes.NotFound, Title = "Data tidak ditemukan.", Status = 404 });
            }

            _items[index] = _items[index] with { IsRead = true };
            return Task.CompletedTask;
        }
    }

    public Task<MarkAllReadResult> MarkAllReadAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var count = _items.Count(i => !i.IsRead);
            for (var i = 0; i < _items.Count; i++)
            {
                _items[i] = _items[i] with { IsRead = true };
            }

            return Task.FromResult(new MarkAllReadResult(count));
        }
    }

    private IEnumerable<NotificationItem> Filter(string? q) =>
        string.IsNullOrWhiteSpace(q) ? _items : _items.Where(i => i.Params.Values.Any(v => v.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)));
}

/// <summary>Mock pengaturan notifikasi: validasi dan konkurensi (<c>RowVersion</c>) meniru API.</summary>
public sealed class MockNotificationSettingsApi : INotificationSettingsApi
{
    private NotificationSettingsDto _state = new(
        NotificationLimits.DefaultDueSoonDays, NotificationLimits.DefaultScheduleTime, NotificationLimits.DefaultRetentionDays,
        NotificationCatalog.All.Select(d => new NotificationTypeSettingDto(d.TypeCode, d.DefaultEnabled)).ToList(), 1, null, null);

    public Task<NotificationSettingsDto> GetAsync(CancellationToken ct = default) => Task.FromResult(_state);

    public Task<NotificationSettingsDto> UpdateAsync(UpdateNotificationSettingsRequest request, CancellationToken ct = default)
    {
        if (request.RowVersion != _state.RowVersion)
        {
            throw new ApiException(new Gms.Shared.Problems.GmsProblemDetails { Type = Gms.Shared.Problems.ProblemTypes.ConcurrencyConflict, Title = "Data telah diubah pengguna lain.", Status = 409 });
        }

        var errors = new Dictionary<string, string[]>();
        if (request.DueSoonDays is < NotificationLimits.DueSoonDaysMin or > NotificationLimits.DueSoonDaysMax)
        {
            errors["dueSoonDays"] = [$"Ambang jatuh tempo harus {NotificationLimits.DueSoonDaysMin} sampai {NotificationLimits.DueSoonDaysMax} hari."];
        }

        if (request.RetentionDays is < NotificationLimits.RetentionDaysMin or > NotificationLimits.RetentionDaysMax)
        {
            errors["retentionDays"] = [$"Masa simpan harus {NotificationLimits.RetentionDaysMin} sampai {NotificationLimits.RetentionDaysMax} hari."];
        }

        if (!TimeOnly.TryParseExact(request.ScheduleTime, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
        {
            errors["scheduleTime"] = ["Jam pemeriksaan harus berformat HH:mm."];
        }

        if (errors.Count > 0)
        {
            throw new ApiException(new Gms.Shared.Problems.GmsProblemDetails
            {
                Type = Gms.Shared.Problems.ProblemTypes.ValidationFailed, Title = "Data tidak valid.", Status = 422, Errors = errors,
            });
        }

        _state = new NotificationSettingsDto(request.DueSoonDays, request.ScheduleTime, request.RetentionDays, request.Types, _state.RowVersion + 1, DateTimeOffset.UtcNow, "Pengguna Uji");
        return Task.FromResult(_state);
    }
}
