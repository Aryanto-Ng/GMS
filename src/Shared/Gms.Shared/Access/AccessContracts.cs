using Gms.Shared.Common;
using Gms.Shared.Enums;

namespace Gms.Shared.Access;

// ===== Autentikasi =====

public sealed record LoginRequest(string Username, string Password, string? IpAddress = null, string? UserAgent = null);

public sealed record CurrentUserDto(Guid Id, string UserName, string FullName, Guid RoleId, string RoleName, bool IsAdministrator);

public sealed record LoginResponse(
    string Token,
    int ExpiresInMinutes,
    CurrentUserDto User,
    bool MustChangePassword,
    IReadOnlyList<string> Permissions);

public sealed record MeResponse(CurrentUserDto User, bool MustChangePassword, IReadOnlyList<string> Permissions);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// ===== Pengguna =====

public sealed record UserListQuery : PageQuery
{
    public Guid? RoleId { get; init; }
    public UserStatus? Status { get; init; }
}

public sealed record UserListItem(
    Guid Id,
    string UserName,
    string FullName,
    Guid RoleId,
    string RoleName,
    UserStatus Status,
    DateTimeOffset? LastLoginAt);

public sealed record UserDetail(
    Guid Id,
    string UserName,
    string FullName,
    Guid RoleId,
    string RoleName,
    UserStatus Status,
    DateTimeOffset? LastLoginAt,
    bool HasActiveSession,
    string? ActiveSessionIp,
    DateTimeOffset? ActiveSessionSince,
    int FailedLoginCount,
    DateTimeOffset? LockedUntil,
    bool IsLastActiveAdministrator,
    uint RowVersion);

public sealed record CreateUserRequest(string UserName, string FullName, Guid RoleId, string InitialPassword);

public sealed record UpdateUserRequest(string FullName, Guid RoleId, uint RowVersion);

/// <summary><see cref="NewPassword"/> null = sistem membuat password sementara acak.</summary>
public sealed record ResetPasswordRequest(string? NewPassword);

/// <summary>Password sementara ditampilkan SEKALI kepada Administrator.</summary>
public sealed record ResetPasswordResponse(string TemporaryPassword);

// ===== Role dan permission =====

public sealed record RoleListItem(Guid Id, string Name, string? Description, bool IsSystem, int UserCount, int PermissionCount);

public sealed record RoleDetail(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    int UserCount,
    IReadOnlyList<string> PermissionKeys,
    uint RowVersion);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> PermissionKeys, uint RowVersion = 0);

public sealed record PermissionItem(
    string Key,
    string Resource,
    string Action,
    string Module,
    string Description,
    int SortOrder,
    bool IsAssignable);

// ===== Audit =====

public sealed record AuditLogQuery : PageQuery
{
    /// <summary>Wajib (AC: filter wajib memakai rentang tanggal).</summary>
    public DateOnly From { get; init; }

    public DateOnly To { get; init; }
    public Guid? ActorId { get; init; }
    public string? EventType { get; init; }
}

public sealed record AuditLogItem(
    long Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string? ActorUsername,
    string EventType,
    string? TargetType,
    string? TargetId,
    string? OldValueJson,
    string? NewValueJson,
    string? IpAddress);

// ===== Antarmuka klien bertipe =====

public interface IAuthApi
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<MeResponse> GetMeAsync(CancellationToken ct = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
}

public interface IUsersApi
{
    Task<PagedResult<UserListItem>> ListAsync(UserListQuery query, CancellationToken ct = default);
    Task<UserDetail> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserDetail> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDetail> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);
    Task<ResetPasswordResponse> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
    Task UnlockAsync(Guid id, CancellationToken ct = default);
    Task EndSessionAsync(Guid id, CancellationToken ct = default);
    Task<UserDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct = default);
}

public interface IRolesApi
{
    Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken ct = default);
    Task<RoleDetail> GetAsync(Guid id, CancellationToken ct = default);
    Task<RoleDetail> CreateAsync(SaveRoleRequest request, CancellationToken ct = default);
    Task<RoleDetail> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PermissionItem>> GetPermissionCatalogAsync(CancellationToken ct = default);
}

public interface IAuditApi
{
    Task<PagedResult<AuditLogItem>> ListAsync(AuditLogQuery query, CancellationToken ct = default);
}
