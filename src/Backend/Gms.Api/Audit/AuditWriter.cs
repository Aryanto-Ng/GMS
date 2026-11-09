using System.Text.Json;
using Gms.Api.Common;
using Gms.Api.Security;
using Gms.Domain.Access;
using Gms.Infrastructure.Persistence;

namespace Gms.Api.Audit;

/// <summary>Jenis peristiwa audit (FSD AC-AM-23). Nilai tersimpan apa adanya di audit_logs.event_type.</summary>
public static class AuditEvents
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string AccountLocked = "AccountLocked";
    public const string Logout = "Logout";
    public const string SessionReplaced = "SessionReplaced";
    public const string SessionEnded = "SessionEnded";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string UserCreated = "UserCreated";
    public const string UserUpdated = "UserUpdated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserActivated = "UserActivated";
    public const string UserUnlocked = "UserUnlocked";
    public const string RoleCreated = "RoleCreated";
    public const string RoleUpdated = "RoleUpdated";
    public const string RoleDeleted = "RoleDeleted";
    public const string AccessDenied = "AccessDenied";

    // Customer PO
    public const string PoCreated = "PoCreated";
    public const string PoUpdated = "PoUpdated";
    public const string PoConfirmed = "PoConfirmed";
    public const string PoCancelled = "PoCancelled";
    public const string PoCustomerNumberChanged = "PoCustomerNumberChanged";
    public const string PoStatusChanged = "PoStatusChanged";
    public const string PoAllocated = "PoAllocated";
    public const string PoAllocationReleased = "PoAllocationReleased";

    // Work Order
    public const string WoCreated = "WoCreated";
    public const string WoUpdated = "WoUpdated";
    public const string WoQuantitiesChanged = "WoQuantitiesChanged";
    public const string WoReleased = "WoReleased";
    public const string WoCompleted = "WoCompleted";
    public const string WoResumed = "WoResumed";
    public const string WoClosed = "WoClosed";
    public const string WoReopened = "WoReopened";
    public const string WoCancelled = "WoCancelled";

    // Production Output
    public const string OutputCreated = "OutputCreated";
    public const string OutputVoided = "OutputVoided";
    public const string OutputCorrected = "OutputCorrected";
    public const string OutputAdjusted = "OutputAdjusted";
    public const string VendorMovementCreated = "VendorMovementCreated";
    public const string VendorMovementVoided = "VendorMovementVoided";
    public const string VendorMovementCorrected = "VendorMovementCorrected";

    // Master Data
    public const string MasterCreated = "MasterCreated";
    public const string MasterUpdated = "MasterUpdated";
    public const string MasterDeactivated = "MasterDeactivated";
    public const string MasterActivated = "MasterActivated";
    public const string FgSizesChanged = "FgSizesChanged";
    public const string FgRoutingChanged = "FgRoutingChanged";
    public const string FgTariffsChanged = "FgTariffsChanged";
    public const string FgCopied = "FgCopied";
    public const string ImportCompleted = "ImportCompleted";
}

public interface IAuditWriter
{
    /// <summary>
    /// Menambahkan catatan audit ke DbContext (TIDAK menyimpan). Pemanggil menyimpan bersama transaksi bisnis
    /// (ADR-009). Dilarang mengisi password, hash, atau token pada nilai.
    /// </summary>
    void Add(
        string eventType,
        string? targetType = null,
        string? targetId = null,
        object? oldValue = null,
        object? newValue = null,
        Guid? actorUserId = null,
        string? actorUsername = null);
}

internal sealed class AuditWriter(GmsDbContext db, ICurrentUser currentUser, IHttpContextAccessor http, TimeProvider time) : IAuditWriter
{
    public void Add(
        string eventType,
        string? targetType = null,
        string? targetId = null,
        object? oldValue = null,
        object? newValue = null,
        Guid? actorUserId = null,
        string? actorUsername = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = time.GetUtcNow(),
            ActorUserId = actorUserId ?? currentUser.Id,
            ActorUsername = actorUsername ?? currentUser.UserName,
            EventType = eventType,
            TargetType = targetType,
            TargetId = targetId,
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue, JsonDefaults.Options),
            NewValue = newValue is null ? null : JsonSerializer.Serialize(newValue, JsonDefaults.Options),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
    }
}
