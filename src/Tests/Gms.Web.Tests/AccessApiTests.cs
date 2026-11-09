using System.Net;
using System.Text;
using Gms.Shared.Access;
using Gms.Shared.Enums;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Gms.Web.Components.Pages.Admin;
using Gms.Web.Infrastructure.Auth;
using Gms.Web.Infrastructure.Http;
using Gms.Web.Services.Api;

namespace Gms.Web.Tests;

public sealed class PermissionMatrixLogicTests
{
    private static PermissionItem P(string resource, string action, int order, bool assignable = true, string module = "Produksi") =>
        new($"{resource}.{action}", resource, action, module, "", order, assignable);

    private static readonly PermissionItem[] Catalog =
    [
        P("Output", "View", 1), P("Output", "Create", 2), P("Output", "Void", 3),
        P("Stok", "View", 4), P("Stok", "Adjust", 5),
        P("User", "View", 6, assignable: false, module: "Administrasi"),
    ];

    [Fact]
    public void Baris_dikelompokkan_per_resource_urut_SortOrder_dan_kolom_gabungan_aksi()
    {
        var rows = PermissionMatrixLogic.Rows(Catalog.Reverse());

        Assert.Equal(["Output", "Stok", "User"], rows.Select(r => r.Resource));
        Assert.Equal(["View", "Create", "Void", "Adjust"], PermissionMatrixLogic.Actions(Catalog.Reverse()));
        Assert.False(rows[2].Assignable);
        Assert.True(rows[0].Assignable);
        Assert.Equal(["View", "Create", "Void"], rows[0].ByAction.Keys);
    }

    [Fact]
    public void Mencentang_aksi_lain_otomatis_mencentang_View_pada_resource_yang_sama()
    {
        var selected = new HashSet<string>();

        var released = PermissionMatrixLogic.Toggle(selected, "Output.Void", on: true);

        Assert.Equal(0, released);
        Assert.Equal(["Output.Void", "Output.View"], selected.OrderByDescending(k => k)); // View ikut tercentang
        Assert.DoesNotContain("Stok.View", selected);
    }

    [Fact]
    public void Melepas_View_melepas_semua_aksi_resource_itu_dan_melaporkan_jumlah_yang_ikut_lepas()
    {
        var selected = new HashSet<string> { "Output.View", "Output.Create", "Output.Void", "Stok.View" };

        var released = PermissionMatrixLogic.Toggle(selected, "Output.View", on: false);

        Assert.Equal(2, released);
        Assert.Equal(["Stok.View"], selected);
    }

    [Fact]
    public void Melepas_aksi_selain_View_tidak_menyentuh_yang_lain()
    {
        var selected = new HashSet<string> { "Output.View", "Output.Create", "Output.Void" };

        var released = PermissionMatrixLogic.Toggle(selected, "Output.Create", on: false);

        Assert.Equal(0, released);
        Assert.Equal(["Output.View", "Output.Void"], selected.Order());
    }

    [Fact]
    public void Aksi_pada_resource_berprefiks_mirip_tidak_ikut_terlepas()
    {
        // "Output" vs "OutputExtra": melepas Output.View tidak boleh menyentuh OutputExtra.*
        var selected = new HashSet<string> { "Output.View", "OutputExtra.View", "OutputExtra.Create" };

        PermissionMatrixLogic.Toggle(selected, "Output.View", on: false);

        Assert.Equal(["OutputExtra.Create", "OutputExtra.View"], selected.Order());
    }

    [Fact]
    public void SetRow_memilih_hanya_aksi_yang_dapat_diberikan_dan_dapat_dilepas_semua()
    {
        var rows = PermissionMatrixLogic.Rows(Catalog);
        var selected = new HashSet<string>();

        PermissionMatrixLogic.SetRow(selected, rows[0], on: true);
        Assert.True(PermissionMatrixLogic.IsRowFull(selected, rows[0]));
        Assert.Equal(3, selected.Count);

        PermissionMatrixLogic.SetRow(selected, rows[2], on: true); // Administrasi: tidak dapat diberikan
        Assert.DoesNotContain("User.View", selected);
        Assert.False(PermissionMatrixLogic.IsRowFull(selected, rows[2]));

        PermissionMatrixLogic.SetRow(selected, rows[0], on: false);
        Assert.Empty(selected);
    }

    [Fact]
    public void Hanya_baca_mempertahankan_View_dan_melepas_aksi_lain_pada_baris_yang_boleh()
    {
        var rows = PermissionMatrixLogic.Rows(Catalog);
        var selected = new HashSet<string> { "Output.Create", "Stok.Adjust" };

        PermissionMatrixLogic.SetReadOnly(selected, rows);

        Assert.Equal(["Output.View", "Stok.View"], selected.Order());
    }

    [Fact]
    public void Diff_menghitung_tambah_dan_lepas()
    {
        Assert.Equal((2, 1), PermissionMatrixLogic.Diff(["A", "B"], ["B", "C", "D"]));
        Assert.Equal((0, 0), PermissionMatrixLogic.Diff(["A"], ["A"]));
    }

    [Fact]
    public void Label_audit_dikenal_dan_kode_tak_dikenal_ditampilkan_apa_adanya()
    {
        Assert.Equal("Login berhasil", AuditLabels.For("LoginSucceeded"));
        Assert.Equal("PoCreated", AuditLabels.For("PoCreated"));
    }
}

public sealed class MockAccessTests
{
    private static async Task<ApiException> Fails(Func<Task> act) => await Assert.ThrowsAsync<ApiException>(act);

    private static (MockUsersApi Users, MockRolesApi Roles, MockAuditApi Audit) Apis()
    {
        var store = new MockAccessStore();
        return (new MockUsersApi(store), new MockRolesApi(store), new MockAuditApi(store));
    }

    [Fact]
    public async Task Daftar_pengguna_mendukung_cari_filter_status_role_dan_paging()
    {
        var (users, _, _) = Apis();

        Assert.Equal(4, (await users.ListAsync(new UserListQuery())).TotalCount);
        Assert.Equal(["siti"], (await users.ListAsync(new UserListQuery { Status = UserStatus.Locked })).Items.Select(u => u.UserName));
        Assert.Equal(["andi"], (await users.ListAsync(new UserListQuery { Status = UserStatus.Inactive })).Items.Select(u => u.UserName));
        Assert.Equal(["admin"], (await users.ListAsync(new UserListQuery { RoleId = MockAccessStore.AdminRoleId })).Items.Select(u => u.UserName));
        Assert.Equal(["staf"], (await users.ListAsync(new UserListQuery { Q = "PENJUALAN" })).Items.Select(u => u.UserName));
        var page = await users.ListAsync(new UserListQuery { Page = 2, PageSize = 3 });
        Assert.Equal((1, 4), (page.Items.Count, page.TotalCount));
    }

    [Fact]
    public async Task Buat_pengguna_menolak_duplikat_password_lemah_dan_role_tak_dikenal()
    {
        var (users, _, _) = Apis();
        var good = new CreateUserRequest("budi", "Budi S", MockAccessStore.StafRoleId, "Abc12345");

        var created = await users.CreateAsync(good);
        Assert.Equal(UserStatus.Active, created.Status);

        var dup = await Fails(() => users.CreateAsync(good with { UserName = "BUDI" }));
        Assert.Equal((409, ProblemTypes.Duplicate), (dup.StatusCode, dup.Problem.Type));

        var weak = await Fails(() => users.CreateAsync(good with { UserName = "weak", InitialPassword = "abcdefgh" }));
        Assert.Equal(422, weak.StatusCode);
        Assert.Contains("initialPassword", weak.Problem.Errors!.Keys);

        var role = await Fails(() => users.CreateAsync(good with { UserName = "norole", RoleId = Guid.NewGuid() }));
        Assert.Contains("roleId", role.Problem.Errors!.Keys);
    }

    [Fact]
    public async Task Ubah_pengguna_memakai_rowVersion_dan_melarang_menurunkan_administrator_terakhir()
    {
        var (users, _, _) = Apis();
        var admin = await users.GetAsync(MockAccessStore.AdminUserId);
        Assert.True(admin.IsLastActiveAdministrator);

        var stale = await Fails(() => users.UpdateAsync(admin.Id, new UpdateUserRequest("X", MockAccessStore.AdminRoleId, admin.RowVersion + 5)));
        Assert.Equal(ProblemTypes.ConcurrencyConflict, stale.Problem.Type);

        var demote = await Fails(() => users.UpdateAsync(admin.Id, new UpdateUserRequest("Admin", MockAccessStore.StafRoleId, admin.RowVersion)));
        Assert.Equal(ProblemCodes.LastAdministrator, demote.Problem.Code);

        var ok = await users.UpdateAsync(admin.Id, new UpdateUserRequest("Admin Baru", MockAccessStore.AdminRoleId, admin.RowVersion));
        Assert.Equal(("Admin Baru", admin.RowVersion + 1), (ok.FullName, ok.RowVersion));
    }

    [Fact]
    public async Task Nonaktifkan_menolak_diri_sendiri_dan_aktifkan_kembali_berfungsi()
    {
        var (users, _, _) = Apis();
        var admin = await users.GetAsync(MockAccessStore.AdminUserId);
        var self = await Fails(() => users.SetActiveAsync(admin.Id, false, admin.RowVersion));
        Assert.Equal(ProblemCodes.CannotDeactivateSelf, self.Problem.Code);

        var staf = (await users.ListAsync(new UserListQuery { Q = "staf" })).Items.Single();
        var detail = await users.GetAsync(staf.Id);
        var off = await users.SetActiveAsync(staf.Id, false, detail.RowVersion);
        Assert.Equal(UserStatus.Inactive, off.Status);
        var on = await users.SetActiveAsync(staf.Id, true, off.RowVersion);
        Assert.Equal(UserStatus.Active, on.Status);
    }

    [Fact]
    public async Task Buka_kunci_reset_password_dan_akhiri_sesi()
    {
        var (users, _, _) = Apis();
        var siti = (await users.ListAsync(new UserListQuery { Status = UserStatus.Locked })).Items.Single();
        Assert.Equal(UserStatus.Locked, (await users.GetAsync(siti.Id)).Status);

        await users.UnlockAsync(siti.Id);
        var after = await users.GetAsync(siti.Id);
        Assert.Equal((UserStatus.Active, 0, null), (after.Status, after.FailedLoginCount, after.LockedUntil));

        var auto = await users.ResetPasswordAsync(siti.Id, new ResetPasswordRequest(null));
        Assert.True(auto.TemporaryPassword.Length >= 8);
        Assert.Equal("Manual123", (await users.ResetPasswordAsync(siti.Id, new ResetPasswordRequest("Manual123"))).TemporaryPassword);
        Assert.Equal(422, (await Fails(() => users.ResetPasswordAsync(siti.Id, new ResetPasswordRequest("x")))).StatusCode);

        await users.EndSessionAsync(MockAccessStore.AdminUserId);
        Assert.False((await users.GetAsync(MockAccessStore.AdminUserId)).HasActiveSession);
        Assert.Equal(404, (await Fails(() => users.GetAsync(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Role_sistem_tidak_dapat_diubah_atau_dihapus_dan_role_terpakai_tidak_dapat_dihapus()
    {
        var (_, roles, _) = Apis();
        var admin = await roles.GetAsync(MockAccessStore.AdminRoleId);

        var edit = await Fails(() => roles.UpdateAsync(admin.Id, new SaveRoleRequest("X", null, [], admin.RowVersion)));
        Assert.Equal(ProblemCodes.SystemRoleImmutable, edit.Problem.Code);
        Assert.Equal(ProblemCodes.SystemRoleImmutable, (await Fails(() => roles.DeleteAsync(admin.Id))).Problem.Code);

        var inUse = await Fails(() => roles.DeleteAsync(MockAccessStore.StafRoleId));
        Assert.Equal(ProblemCodes.RoleInUse, inUse.Problem.Code);

        var created = await roles.CreateAsync(new SaveRoleRequest("Kosong", null, []));
        await roles.DeleteAsync(created.Id);
        Assert.Equal(404, (await Fails(() => roles.GetAsync(created.Id))).StatusCode);
    }

    [Fact]
    public async Task Simpan_role_memvalidasi_nama_duplikat_izin_tak_dikenal_tak_dapat_diberikan_dan_View_wajib()
    {
        var (_, roles, _) = Apis();

        Assert.Contains("name", (await Fails(() => roles.CreateAsync(new SaveRoleRequest(" ", null, [])))).Problem.Errors!.Keys);
        Assert.Equal(409, (await Fails(() => roles.CreateAsync(new SaveRoleRequest("staf comfi", null, [])))).StatusCode);
        Assert.Contains("permissionKeys", (await Fails(() => roles.CreateAsync(new SaveRoleRequest("A", null, ["Tidak.Ada"])))).Problem.Errors!.Keys);

        var admin = PermissionCatalog.All.First(p => !p.IsAssignable);
        Assert.Contains("hanya untuk Administrator", (await Fails(() => roles.CreateAsync(new SaveRoleRequest("B", null, [admin.Key])))).Problem.Errors!["permissionKeys"][0]);

        var needsView = await Fails(() => roles.CreateAsync(new SaveRoleRequest("C", null, [Permissions.CustomerPO.Create])));
        Assert.Contains("membutuhkan", needsView.Problem.Errors!["permissionKeys"][0]);

        var ok = await roles.CreateAsync(new SaveRoleRequest("D", "desk", [Permissions.CustomerPO.View, Permissions.CustomerPO.Create]));
        Assert.Equal(2, ok.PermissionKeys.Count);
        var updated = await roles.UpdateAsync(ok.Id, new SaveRoleRequest("D2", null, [Permissions.CustomerPO.View], ok.RowVersion));
        Assert.Equal(("D2", 1), (updated.Name, updated.PermissionKeys.Count));
        Assert.Equal(409, (await Fails(() => roles.UpdateAsync(ok.Id, new SaveRoleRequest("D3", null, [], ok.RowVersion)))).StatusCode); // basi
    }

    [Fact]
    public async Task Katalog_izin_dikirim_dari_server_dan_audit_mencatat_aksi_serta_membatasi_rentang()
    {
        var (users, roles, audit) = Apis();
        Assert.Equal(PermissionCatalog.All.Count, (await roles.GetPermissionCatalogAsync()).Count);
        await users.CreateAsync(new CreateUserRequest("audit1", "A", MockAccessStore.StafRoleId, "Abc12345"));
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);

        var all = await audit.ListAsync(new AuditLogQuery { From = today.AddDays(-1), To = today.AddDays(1) });
        Assert.Contains(all.Items, a => a.EventType == "UserCreated" && a.TargetId == "audit1");
        Assert.True(all.Items.Zip(all.Items.Skip(1)).All(p => p.First.OccurredAt >= p.Second.OccurredAt)); // terbaru dahulu
        Assert.All((await audit.ListAsync(new AuditLogQuery { From = today.AddDays(-1), To = today.AddDays(1), EventType = "UserCreated" })).Items, a => Assert.Equal("UserCreated", a.EventType));
        Assert.Empty((await audit.ListAsync(new AuditLogQuery { From = today.AddDays(-10), To = today.AddDays(-5) })).Items);

        Assert.Equal(422, (await Fails(() => audit.ListAsync(new AuditLogQuery { From = today, To = today.AddDays(-1) }))).StatusCode);
        Assert.Equal(400, (await Fails(() => audit.ListAsync(new AuditLogQuery { From = today.AddDays(-200), To = today }))).StatusCode);
    }
}

public sealed class HttpAccessApiTests
{
    private sealed class Token : ISessionTokenProvider
    {
        public ValueTask<string?> GetTokenAsync() => ValueTask.FromResult<string?>("t");
        public void Override(string? value) { }
    }

    private sealed class Recorder : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpMethod Method, string Url, string? Body)> Calls { get; } = [];

        public string Reply { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Pengguna_memakai_rute_query_dan_metode_yang_benar()
    {
        var http = new Recorder { Reply = "{\"items\":[],\"page\":1,\"pageSize\":25,\"totalCount\":0}" };
        var api = new HttpUsersApi(new GmsApiClient(http, new Token()));

        await api.ListAsync(new UserListQuery { Page = 2, Q = "a b", RoleId = Id, Status = UserStatus.Locked });
        http.Reply = "{}";
        await api.UnlockAsync(Id);
        await api.EndSessionAsync(Id);
        await api.SetActiveAsync(Id, false, 7);
        await api.SetActiveAsync(Id, true, 8);
        await api.UpdateAsync(Id, new UpdateUserRequest("N", Id, 3));

        Assert.Equal($"/api/v1/users?page=2&pageSize=25&q=a%20b&roleId={Id}&status=Locked", http.Calls[0].Url);
        Assert.Equal(($"/api/v1/users/{Id}/unlock", HttpMethod.Post), (http.Calls[1].Url, http.Calls[1].Method));
        Assert.Equal($"/api/v1/users/{Id}/end-session", http.Calls[2].Url);
        Assert.Equal($"/api/v1/users/{Id}/deactivate?rowVersion=7", http.Calls[3].Url);
        Assert.Equal($"/api/v1/users/{Id}/activate?rowVersion=8", http.Calls[4].Url);
        Assert.Equal((HttpMethod.Put, $"/api/v1/users/{Id}"), (http.Calls[5].Method, http.Calls[5].Url));
        Assert.Contains("\"rowVersion\":3", http.Calls[5].Body);
    }

    [Fact]
    public async Task Role_dan_audit_memakai_rute_yang_benar_termasuk_DELETE()
    {
        var http = new Recorder { Reply = "[]" };
        var roles = new HttpRolesApi(new GmsApiClient(http, new Token()));
        await roles.ListAsync();
        await roles.GetPermissionCatalogAsync();
        await roles.DeleteAsync(Id);
        http.Reply = "{\"items\":[],\"page\":1,\"pageSize\":25,\"totalCount\":0}";
        await new HttpAuditApi(new GmsApiClient(http, new Token())).ListAsync(
            new AuditLogQuery { From = new DateOnly(2026, 11, 1), To = new DateOnly(2026, 11, 8), EventType = "RoleUpdated" });

        Assert.Equal("/api/v1/roles", http.Calls[0].Url);
        Assert.Equal("/api/v1/permissions", http.Calls[1].Url);
        Assert.Equal((HttpMethod.Delete, $"/api/v1/roles/{Id}"), (http.Calls[2].Method, http.Calls[2].Url));
        Assert.Equal("/api/v1/audit-logs?page=1&pageSize=25&from=2026-11-01&to=2026-11-08&eventType=RoleUpdated", http.Calls[3].Url);
    }
}
