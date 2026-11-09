using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Security;

namespace Gms.Web.Services.Api;

/// <summary>Data tiruan Access Management bersama (statis) untuk pengembangan UI tanpa API/DB (ADR-014). Galat meniru API: <see cref="ApiException"/> + Problem Details.</summary>
public sealed class MockAccessStore
{
    public static readonly Guid AdminRoleId = Guid.Parse("00000000-0000-0000-0000-00000000aa01");
    public static readonly Guid StafRoleId = Guid.Parse("00000000-0000-0000-0000-00000000aa02");
    public static readonly Guid AdminUserId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private sealed class UserRow
    {
        public Guid Id; public string UserName = ""; public string FullName = ""; public Guid RoleId; public bool Active = true;
        public DateTimeOffset? LastLogin; public bool Session; public string? Ip; public DateTimeOffset? SessionSince;
        public int Failed; public DateTimeOffset? LockedUntil; public uint Version = 1;
    }

    private sealed class RoleRow
    {
        public Guid Id; public string Name = ""; public string? Description; public bool System; public List<string> Keys = []; public uint Version = 1;
    }

    private readonly object _gate = new();
    private readonly List<UserRow> _users;
    private readonly List<RoleRow> _roles;
    private readonly List<AuditLogItem> _audit = [];
    private long _auditId = 1000;

    public MockAccessStore()
    {
        var now = DateTimeOffset.UtcNow;
        _roles =
        [
            new RoleRow { Id = AdminRoleId, Name = "Administrator", Description = "Akses penuh (role sistem)", System = true, Keys = PermissionCatalog.All.Select(p => p.Key).ToList() },
            new RoleRow
            {
                Id = StafRoleId, Name = "Staf Comfi", Description = "Melihat PO dan stok FG",
                Keys = [Permissions.CustomerPO.View, Permissions.StokFG.View],
            },
        ];
        _users =
        [
            new UserRow { Id = AdminUserId, UserName = "admin", FullName = "Administrator", RoleId = AdminRoleId, LastLogin = now.AddHours(-1), Session = true, Ip = "10.0.0.2", SessionSince = now.AddHours(-1) },
            new UserRow { Id = Guid.Parse("00000000-0000-0000-0000-0000000000b2"), UserName = "staf", FullName = "Staf Penjualan", RoleId = StafRoleId, LastLogin = now.AddDays(-1) },
            new UserRow { Id = Guid.Parse("00000000-0000-0000-0000-0000000000c3"), UserName = "siti", FullName = "Siti Rahayu", RoleId = StafRoleId, Failed = 5, LockedUntil = now.AddMinutes(12), LastLogin = now.AddDays(-2) },
            new UserRow { Id = Guid.Parse("00000000-0000-0000-0000-0000000000d4"), UserName = "andi", FullName = "Andi Pratama", RoleId = StafRoleId, Active = false, LastLogin = now.AddDays(-9) },
        ];
        Log("LoginSucceeded", "admin", null, null, "10.0.0.2");
        Log("UserCreated", "admin", "User", "siti", "10.0.0.2");
        Log("AccountLocked", null, "User", "siti", "10.0.0.9");
    }

    public static ApiException Problem(string type, int status, string title, string? detail = null, string? code = null, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(new GmsProblemDetails { Type = type, Status = status, Title = title, Detail = detail, Code = code, Errors = errors, TraceId = "mock-" + Guid.NewGuid().ToString("N")[..8] });

    private static ApiException NotFound(string what) => Problem(ProblemTypes.NotFound, 404, $"{what} tidak ditemukan.");

    private static ApiException Stale() => Problem(ProblemTypes.ConcurrencyConflict, 409, "Data telah diubah pengguna lain.", "Muat ulang halaman lalu ulangi perubahan Anda.");

    private static ApiException Invalid(string field, string message) =>
        Problem(ProblemTypes.ValidationFailed, 422, "Data tidak valid.", "Periksa kembali isian yang ditandai.", errors: new Dictionary<string, string[]> { [field] = [message] });

    private void Log(string type, string? actor, string? target, string? targetId, string? ip, string? oldJson = null, string? newJson = null) =>
        _audit.Add(new AuditLogItem(++_auditId, DateTimeOffset.UtcNow, actor is null ? null : Guid.NewGuid(), actor, type, target, targetId, oldJson, newJson, ip));

    private string RoleName(Guid id) => _roles.FirstOrDefault(r => r.Id == id)?.Name ?? "-";

    private static UserStatus StatusOf(UserRow u) =>
        !u.Active ? UserStatus.Inactive : u.LockedUntil > DateTimeOffset.UtcNow ? UserStatus.Locked : UserStatus.Active;

    private UserDetail Detail(UserRow u) => new(
        u.Id, u.UserName, u.FullName, u.RoleId, RoleName(u.RoleId), StatusOf(u), u.LastLogin, u.Session, u.Ip, u.SessionSince,
        u.Failed, u.LockedUntil > DateTimeOffset.UtcNow ? u.LockedUntil : null, IsLastAdmin(u), u.Version);

    private bool IsLastAdmin(UserRow u) =>
        u.Active && u.RoleId == AdminRoleId && _users.Count(x => x.Active && x.RoleId == AdminRoleId) == 1;

    private static void CheckPassword(string password, string field)
    {
        if (password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw Invalid(field, "Password minimal 8 karakter dengan huruf dan angka.");
        }
    }

    // ---- Pengguna ----

    public PagedResult<UserListItem> ListUsers(UserListQuery q)
    {
        lock (_gate)
        {
            var query = _users.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(q.Q))
            {
                query = query.Where(u => u.UserName.Contains(q.Q, StringComparison.OrdinalIgnoreCase) || u.FullName.Contains(q.Q, StringComparison.OrdinalIgnoreCase));
            }

            if (q.RoleId is { } role) { query = query.Where(u => u.RoleId == role); }
            if (q.Status is { } status) { query = query.Where(u => StatusOf(u) == status); }

            var all = query.OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).ToList();
            var items = all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
                .Select(u => new UserListItem(u.Id, u.UserName, u.FullName, u.RoleId, RoleName(u.RoleId), StatusOf(u), u.LastLogin)).ToList();
            return new PagedResult<UserListItem>(items, q.Page, q.PageSize, all.Count);
        }
    }

    public UserDetail GetUser(Guid id)
    {
        lock (_gate)
        {
            return Detail(_users.FirstOrDefault(u => u.Id == id) ?? throw NotFound("Pengguna"));
        }
    }

    public UserDetail CreateUser(CreateUserRequest r)
    {
        lock (_gate)
        {
            var name = r.UserName?.Trim() ?? string.Empty;
            if (name.Length < 3) { throw Invalid("userName", "Username minimal 3 karakter."); }
            if (string.IsNullOrWhiteSpace(r.FullName)) { throw Invalid("fullName", "Nama lengkap wajib diisi."); }
            if (_roles.All(x => x.Id != r.RoleId)) { throw Invalid("roleId", "Role tidak ditemukan."); }
            CheckPassword(r.InitialPassword ?? string.Empty, "initialPassword");
            if (_users.Any(u => u.UserName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw Problem(ProblemTypes.Duplicate, 409, "Username sudah dipakai.", $"Username \"{name}\" sudah terdaftar.");
            }

            var user = new UserRow { Id = Guid.NewGuid(), UserName = name, FullName = r.FullName.Trim(), RoleId = r.RoleId };
            _users.Add(user);
            Log("UserCreated", "admin", "User", name, null);
            return Detail(user);
        }
    }

    public UserDetail UpdateUser(Guid id, UpdateUserRequest r)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == id) ?? throw NotFound("Pengguna");
            if (user.Version != r.RowVersion) { throw Stale(); }
            if (string.IsNullOrWhiteSpace(r.FullName)) { throw Invalid("fullName", "Nama lengkap wajib diisi."); }
            if (_roles.All(x => x.Id != r.RoleId)) { throw Invalid("roleId", "Role tidak ditemukan."); }
            if (IsLastAdmin(user) && r.RoleId != AdminRoleId)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Administrator terakhir tidak dapat diturunkan.", "Harus ada minimal satu Administrator aktif.", ProblemCodes.LastAdministrator);
            }

            user.FullName = r.FullName.Trim();
            user.RoleId = r.RoleId;
            user.Version++;
            Log("UserUpdated", "admin", "User", user.UserName, null);
            return Detail(user);
        }
    }

    public ResetPasswordResponse ResetPassword(Guid id, ResetPasswordRequest r)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == id) ?? throw NotFound("Pengguna");
            if (r.NewPassword is not null) { CheckPassword(r.NewPassword, "newPassword"); }
            Log("PasswordReset", "admin", "User", user.UserName, null);
            return new ResetPasswordResponse(r.NewPassword ?? "Tmp-" + Guid.NewGuid().ToString("N")[..8] + "7a");
        }
    }

    public void Unlock(Guid id)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == id) ?? throw NotFound("Pengguna");
            user.Failed = 0;
            user.LockedUntil = null;
            user.Version++;
            Log("UserUnlocked", "admin", "User", user.UserName, null);
        }
    }

    public void EndSession(Guid id)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == id) ?? throw NotFound("Pengguna");
            user.Session = false;
            user.Ip = null;
            user.SessionSince = null;
            Log("SessionEnded", "admin", "User", user.UserName, null);
        }
    }

    public UserDetail SetActive(Guid id, bool active, uint rowVersion)
    {
        lock (_gate)
        {
            var user = _users.FirstOrDefault(u => u.Id == id) ?? throw NotFound("Pengguna");
            if (user.Version != rowVersion) { throw Stale(); }
            if (!active && user.Id == AdminUserId)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Anda tidak dapat menonaktifkan diri sendiri.", code: ProblemCodes.CannotDeactivateSelf);
            }

            if (!active && IsLastAdmin(user))
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Administrator terakhir tidak dapat dinonaktifkan.", code: ProblemCodes.LastAdministrator);
            }

            user.Active = active;
            if (!active) { user.Session = false; }
            user.Version++;
            Log(active ? "UserActivated" : "UserDeactivated", "admin", "User", user.UserName, null);
            return Detail(user);
        }
    }

    // ---- Role ----

    private RoleListItem Item(RoleRow r) => new(r.Id, r.Name, r.Description, r.System, _users.Count(u => u.RoleId == r.Id), r.Keys.Count);

    private RoleDetail Detail(RoleRow r) => new(r.Id, r.Name, r.Description, r.System, _users.Count(u => u.RoleId == r.Id), r.Keys.ToList(), r.Version);

    public IReadOnlyList<RoleListItem> ListRoles()
    {
        lock (_gate)
        {
            return _roles.OrderByDescending(r => r.System).ThenBy(r => r.Name).Select(Item).ToList();
        }
    }

    public RoleDetail GetRole(Guid id)
    {
        lock (_gate)
        {
            return Detail(_roles.FirstOrDefault(r => r.Id == id) ?? throw NotFound("Role"));
        }
    }

    public IReadOnlyList<PermissionItem> Catalog() =>
        PermissionCatalog.All.Select(p => new PermissionItem(p.Key, p.Resource, p.Action, p.Module, p.Description, p.SortOrder, p.IsAssignable)).ToList();

    private void ValidateRole(SaveRoleRequest r, Guid? self)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) { throw Invalid("name", "Nama role wajib diisi."); }
        if (_roles.Any(x => x.Id != self && x.Name.Equals(r.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw Problem(ProblemTypes.Duplicate, 409, "Nama role sudah dipakai.");
        }

        var catalog = PermissionCatalog.All.ToDictionary(p => p.Key);
        foreach (var key in r.PermissionKeys)
        {
            if (!catalog.TryGetValue(key, out var def)) { throw Invalid("permissionKeys", $"Permission tidak dikenal: {key}."); }
            if (!def.IsAssignable) { throw Invalid("permissionKeys", $"Permission {key} hanya untuk Administrator."); }
            if (PermissionCatalog.RequiredViewFor(key) is { } view && !r.PermissionKeys.Contains(view))
            {
                throw Invalid("permissionKeys", $"{key} membutuhkan {view}.");
            }
        }
    }

    public RoleDetail CreateRole(SaveRoleRequest r)
    {
        lock (_gate)
        {
            ValidateRole(r, null);
            var role = new RoleRow { Id = Guid.NewGuid(), Name = r.Name.Trim(), Description = r.Description?.Trim(), Keys = r.PermissionKeys.Distinct().ToList() };
            _roles.Add(role);
            Log("RoleCreated", "admin", "Role", role.Name, null);
            return Detail(role);
        }
    }

    public RoleDetail UpdateRole(Guid id, SaveRoleRequest r)
    {
        lock (_gate)
        {
            var role = _roles.FirstOrDefault(x => x.Id == id) ?? throw NotFound("Role");
            if (role.System)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Role sistem tidak dapat diubah.", code: ProblemCodes.SystemRoleImmutable);
            }

            if (role.Version != r.RowVersion) { throw Stale(); }
            ValidateRole(r, id);
            var before = string.Join(",", role.Keys.Order());
            role.Name = r.Name.Trim();
            role.Description = r.Description?.Trim();
            role.Keys = r.PermissionKeys.Distinct().ToList();
            role.Version++;
            Log("RoleUpdated", "admin", "Role", role.Name, null, before, string.Join(",", role.Keys.Order()));
            return Detail(role);
        }
    }

    public void DeleteRole(Guid id)
    {
        lock (_gate)
        {
            var role = _roles.FirstOrDefault(x => x.Id == id) ?? throw NotFound("Role");
            if (role.System)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Role sistem tidak dapat dihapus.", code: ProblemCodes.SystemRoleImmutable);
            }

            if (_users.Count(u => u.RoleId == id) is var n and > 0)
            {
                throw Problem(ProblemTypes.RuleViolation, 409, "Role masih dipakai.", $"Role dipakai oleh {n} pengguna.", ProblemCodes.RoleInUse);
            }

            _roles.Remove(role);
            Log("RoleDeleted", "admin", "Role", role.Name, null);
        }
    }

    // ---- Audit ----

    public PagedResult<AuditLogItem> ListAudit(AuditLogQuery q)
    {
        lock (_gate)
        {
            if (q.To < q.From) { throw Invalid("to", "Tanggal akhir tidak boleh sebelum tanggal awal."); }
            if (q.To.DayNumber - q.From.DayNumber > 92)
            {
                throw Problem(ProblemTypes.BadRequest, 400, "Rentang tanggal terlalu lebar.", "Persempit rentang maksimal 3 bulan.");
            }

            var query = _audit.Where(a =>
            {
                var d = DateOnly.FromDateTime(a.OccurredAt.ToOffset(TimeSpan.FromHours(7)).DateTime);
                return d >= q.From && d <= q.To;
            });
            if (!string.IsNullOrEmpty(q.EventType)) { query = query.Where(a => a.EventType == q.EventType); }
            if (q.ActorId is { } actor) { query = query.Where(a => a.ActorUserId == actor); }
            var all = query.OrderByDescending(a => a.OccurredAt).ToList();
            return new PagedResult<AuditLogItem>(all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), q.Page, q.PageSize, all.Count);
        }
    }
}

public sealed class MockUsersApi(MockAccessStore store) : IUsersApi
{
    public Task<PagedResult<UserListItem>> ListAsync(UserListQuery query, CancellationToken ct = default) => Task.FromResult(store.ListUsers(query));
    public Task<UserDetail> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(store.GetUser(id));
    public Task<UserDetail> CreateAsync(CreateUserRequest request, CancellationToken ct = default) => Task.FromResult(store.CreateUser(request));
    public Task<UserDetail> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default) => Task.FromResult(store.UpdateUser(id, request));
    public Task<ResetPasswordResponse> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default) => Task.FromResult(store.ResetPassword(id, request));
    public Task UnlockAsync(Guid id, CancellationToken ct = default) { store.Unlock(id); return Task.CompletedTask; }
    public Task EndSessionAsync(Guid id, CancellationToken ct = default) { store.EndSession(id); return Task.CompletedTask; }
    public Task<UserDetail> SetActiveAsync(Guid id, bool isActive, uint rowVersion, CancellationToken ct = default) => Task.FromResult(store.SetActive(id, isActive, rowVersion));
}

public sealed class MockRolesApi(MockAccessStore store) : IRolesApi
{
    public Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken ct = default) => Task.FromResult(store.ListRoles());
    public Task<RoleDetail> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(store.GetRole(id));
    public Task<RoleDetail> CreateAsync(SaveRoleRequest request, CancellationToken ct = default) => Task.FromResult(store.CreateRole(request));
    public Task<RoleDetail> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken ct = default) => Task.FromResult(store.UpdateRole(id, request));
    public Task DeleteAsync(Guid id, CancellationToken ct = default) { store.DeleteRole(id); return Task.CompletedTask; }
    public Task<IReadOnlyList<PermissionItem>> GetPermissionCatalogAsync(CancellationToken ct = default) => Task.FromResult(store.Catalog());
}

public sealed class MockAuditApi(MockAccessStore store) : IAuditApi
{
    public Task<PagedResult<AuditLogItem>> ListAsync(AuditLogQuery query, CancellationToken ct = default) => Task.FromResult(store.ListAudit(query));
}
