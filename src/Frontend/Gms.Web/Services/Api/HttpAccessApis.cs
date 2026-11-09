using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Web.Infrastructure.Http;

namespace Gms.Web.Services.Api;

/// <summary>Implementasi <see cref="IUsersApi"/> ke API sungguhan (<c>/api/v1/users</c>).</summary>
public sealed class HttpUsersApi(GmsApiClient client) : IUsersApi
{
    public Task<PagedResult<UserListItem>> ListAsync(UserListQuery query, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<UserListItem>>("/api/v1/users" + ApiQuery.Build(
            ("page", query.Page), ("pageSize", query.PageSize), ("q", query.Q), ("sort", query.Sort), ("roleId", query.RoleId), ("status", query.Status)), ct);

    public Task<UserDetail> GetAsync(Guid id, CancellationToken ct = default) => client.GetAsync<UserDetail>($"/api/v1/users/{id}", ct);

    public Task<UserDetail> CreateAsync(CreateUserRequest request, CancellationToken ct = default) =>
        client.PostAsync<UserDetail>("/api/v1/users", request, ct);

    public Task<UserDetail> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default) =>
        client.PutAsync<UserDetail>($"/api/v1/users/{id}", request, ct);

    public Task<ResetPasswordResponse> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default) =>
        client.PostAsync<ResetPasswordResponse>($"/api/v1/users/{id}/reset-password", request, ct);

    public Task UnlockAsync(Guid id, CancellationToken ct = default) => client.PostAsync($"/api/v1/users/{id}/unlock", null, ct);

    public Task EndSessionAsync(Guid id, CancellationToken ct = default) => client.PostAsync($"/api/v1/users/{id}/end-session", null, ct);

    public Task<UserDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct = default) =>
        client.PostAsync<UserDetail>($"/api/v1/users/{id}/{(isActive ? "activate" : "deactivate")}?rowVersion={rowVersion}", null, ct);
}

/// <summary>Implementasi <see cref="IRolesApi"/> ke API sungguhan (<c>/api/v1/roles</c>, <c>/api/v1/permissions</c>).</summary>
public sealed class HttpRolesApi(GmsApiClient client) : IRolesApi
{
    public Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken ct = default) =>
        client.GetAsync<IReadOnlyList<RoleListItem>>("/api/v1/roles", ct);

    public Task<RoleDetail> GetAsync(Guid id, CancellationToken ct = default) => client.GetAsync<RoleDetail>($"/api/v1/roles/{id}", ct);

    public Task<RoleDetail> CreateAsync(SaveRoleRequest request, CancellationToken ct = default) =>
        client.PostAsync<RoleDetail>("/api/v1/roles", request, ct);

    public Task<RoleDetail> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken ct = default) =>
        client.PutAsync<RoleDetail>($"/api/v1/roles/{id}", request, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => client.DeleteAsync($"/api/v1/roles/{id}", ct);

    public Task<IReadOnlyList<PermissionItem>> GetPermissionCatalogAsync(CancellationToken ct = default) =>
        client.GetAsync<IReadOnlyList<PermissionItem>>("/api/v1/permissions", ct);
}

/// <summary>Implementasi <see cref="IAuditApi"/> ke API sungguhan (<c>/api/v1/audit-logs</c>).</summary>
public sealed class HttpAuditApi(GmsApiClient client) : IAuditApi
{
    public Task<PagedResult<AuditLogItem>> ListAsync(AuditLogQuery query, CancellationToken ct = default) =>
        client.GetAsync<PagedResult<AuditLogItem>>("/api/v1/audit-logs" + ApiQuery.Build(
            ("page", query.Page), ("pageSize", query.PageSize), ("from", query.From), ("to", query.To), ("actorId", query.ActorId), ("eventType", query.EventType)), ct);
}
