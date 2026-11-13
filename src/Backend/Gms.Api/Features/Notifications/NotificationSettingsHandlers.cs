using System.Globalization;
using Gms.Api.Audit;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Notifications;
using Gms.Infrastructure.Persistence;
using Gms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Features.Notifications;

public sealed class GetNotificationSettingsHandler(GmsDbContext db)
{
    public async Task<NotificationSettingsDto> HandleAsync(CancellationToken ct)
    {
        var options = await db.NotificationOptionsRows.AsNoTracking().SingleAsync(ct);
        var types = await db.NotificationTypeSettings.AsNoTracking().ToDictionaryAsync(t => t.TypeCode, t => t.Enabled, StringComparer.Ordinal, ct);
        string? updatedBy = options.UpdatedBy is { } uid
            ? await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            : null;
        return new NotificationSettingsDto(
            options.DueSoonDays,
            options.ScheduleTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            options.RetentionDays,
            NotificationCatalog.All.Select(d => new NotificationTypeSettingDto(d.TypeCode, types.GetValueOrDefault(d.TypeCode, d.DefaultEnabled))).ToList(),
            options.RowVersion,
            options.UpdatedAt,
            updatedBy);
    }
}

public sealed class UpdateNotificationSettingsHandler(
    GmsDbContext db,
    ICurrentUser user,
    IAuditWriter audit,
    TimeProvider time,
    GetNotificationSettingsHandler reader)
{
    public async Task<NotificationSettingsDto> HandleAsync(UpdateNotificationSettingsRequest request, CancellationToken ct)
    {
        var errors = new ErrorBag();
        if (request.DueSoonDays is < NotificationLimits.DueSoonDaysMin or > NotificationLimits.DueSoonDaysMax)
        {
            errors.Add("dueSoonDays", $"Ambang jatuh tempo harus {NotificationLimits.DueSoonDaysMin} sampai {NotificationLimits.DueSoonDaysMax} hari.");
        }

        if (request.RetentionDays is < NotificationLimits.RetentionDaysMin or > NotificationLimits.RetentionDaysMax)
        {
            errors.Add("retentionDays", $"Masa simpan harus {NotificationLimits.RetentionDaysMin} sampai {NotificationLimits.RetentionDaysMax} hari.");
        }

        if (!TimeOnly.TryParseExact(request.ScheduleTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var schedule))
        {
            errors.Add("scheduleTime", "Jam pemeriksaan harus berformat HH:mm.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < request.Types.Count; i++)
        {
            var code = request.Types[i].TypeCode;
            if (NotificationCatalog.Find(code) is null)
            {
                errors.Add($"types[{i}].typeCode", "Jenis notifikasi tidak dikenal.");
            }
            else if (!seen.Add(code))
            {
                errors.Add($"types[{i}].typeCode", "Jenis notifikasi dobel.");
            }
        }

        errors.ThrowIfAny();

        var options = await db.NotificationOptionsRows.SingleAsync(ct);
        db.Entry(options).Property(x => x.RowVersion).OriginalValue = request.RowVersion;
        var types = await db.NotificationTypeSettings.ToDictionaryAsync(t => t.TypeCode, StringComparer.Ordinal, ct);

        var before = new
        {
            options.DueSoonDays,
            ScheduleTime = options.ScheduleTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            options.RetentionDays,
            Types = types.OrderBy(t => t.Key, StringComparer.Ordinal).ToDictionary(t => t.Key, t => t.Value.Enabled),
        };

        var now = time.GetUtcNow();
        options.DueSoonDays = request.DueSoonDays;
        options.ScheduleTime = schedule;
        options.RetentionDays = request.RetentionDays;
        // UPDATE selalu dikirim (UpdatedAt berubah) sehingga rowVersion klien diperiksa dan versi naik pada setiap simpan,
        // termasuk bila yang berubah hanya pengaktifan jenis.
        options.UpdatedAt = now;
        options.UpdatedBy = user.Id;

        foreach (var toggle in request.Types)
        {
            if (!types.TryGetValue(toggle.TypeCode, out var row))
            {
                row = new NotificationTypeSetting { TypeCode = toggle.TypeCode, Enabled = toggle.Enabled, CreatedAt = now, CreatedBy = user.Id };
                db.NotificationTypeSettings.Add(row);
                types[toggle.TypeCode] = row;
            }
            else if (row.Enabled != toggle.Enabled)
            {
                row.Enabled = toggle.Enabled;
                row.UpdatedAt = now;
                row.UpdatedBy = user.Id;
            }
        }

        audit.Add(
            AuditEvents.NotificationSettingsChanged,
            "NotificationSettings",
            "1",
            before,
            new
            {
                options.DueSoonDays,
                ScheduleTime = request.ScheduleTime,
                options.RetentionDays,
                Types = types.OrderBy(t => t.Key, StringComparer.Ordinal).ToDictionary(t => t.Key, t => t.Value.Enabled),
            });

        await db.SaveChangesAsync(ct);
        return await reader.HandleAsync(ct);
    }
}
