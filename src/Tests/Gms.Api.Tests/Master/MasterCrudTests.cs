using System.Net;
using System.Text.Json;
using Gms.Api.Tests.Infrastructure;
using Gms.Shared.Common;
using Gms.Shared.Master;
using Gms.Shared.Problems;
using Gms.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Gms.Api.Tests.Master;

/// <summary>Perilaku bersama semua master (FSD Master Data bagian 3): diuji terhadap SEMUA entitas dengan Theory.</summary>
[Collection(ApiCollection.Name)]
public sealed class MasterCrudTests(ApiFixture fixture)
{
    private TestHost H => fixture.Main;

    public sealed record Spec(string Route, string View, string Create, string? CodePrefix, int MaxCode);

    private static readonly Dictionary<string, Spec> Specs = new()
    {
        ["uoms"] = new("uoms", Permissions.MasterData.UoM.View, Permissions.MasterData.UoM.Create, null, 20),
        ["material-categories"] = new("material-categories", Permissions.MasterData.KategoriMaterial.View, Permissions.MasterData.KategoriMaterial.Create, null, 30),
        ["materials"] = new("materials", Permissions.MasterData.Material.View, Permissions.MasterData.Material.Create, null, 50),
        ["sizes"] = new("sizes", Permissions.MasterData.Size.View, Permissions.MasterData.Size.Create, null, 20),
        ["colors"] = new("colors", Permissions.MasterData.Color.View, Permissions.MasterData.Color.Create, null, 30),
        ["customers"] = new("customers", Permissions.MasterData.Customer.View, Permissions.MasterData.Customer.Create, "CUS", 0),
        ["processes"] = new("processes", Permissions.MasterData.Process.View, Permissions.MasterData.Process.Create, null, 30),
        ["vendors"] = new("vendors", Permissions.MasterData.Vendor.View, Permissions.MasterData.Vendor.Create, "VND", 0),
        ["workers"] = new("workers", Permissions.MasterData.Pekerja.View, Permissions.MasterData.Pekerja.Create, "PKJ", 0),
        ["fgs"] = new("fgs", Permissions.MasterData.FG.View, Permissions.MasterData.FG.Create, null, 50),
    };

    public static TheoryData<string> AllRoutes => [.. Specs.Keys];
    public static TheoryData<string> ManualCodeRoutes => [.. Specs.Values.Where(s => s.CodePrefix is null).Select(s => s.Route)];
    public static TheoryData<string> AutoCodeRoutes => [.. Specs.Values.Where(s => s.CodePrefix is not null).Select(s => s.Route)];

    /// <summary>Isi permintaan simpan untuk sebuah entitas; <paramref name="rowVersion"/> 0 untuk pembuatan baru.</summary>
    private async Task<object> BodyAsync(string route, string? code, string name, uint rowVersion = 0)
    {
        switch (route)
        {
            case "uoms":
            case "material-categories":
                return new { code, name, rowVersion };
            case "sizes":
                return new { code, name, sortOrder = 15, rowVersion };
            case "colors":
                return new { code, name, hex = "#aabbcc", rowVersion };
            case "processes":
                return new { code, name, type = "Internal", rowVersion };
            case "fgs":
                return new { code, name, kind = "Kemeja", customerId = (Guid?)null, notes = (string?)null, rowVersion };
            case "materials":
            {
                var category = await H.NewCategoryAsync();
                var uom = await H.NewUomAsync();
                return new { code, name, categoryId = category.Id, uomId = uom.Id, description = "uji", rowVersion };
            }

            case "customers":
                return new { name, contactPerson = "PIC", phone = "0812", address = "Jl. Uji", notes = (string?)null, rowVersion };
            case "vendors":
                return new { name, serviceType = "Bordir", contactPerson = "PIC", phone = "0812", address = (string?)null, notes = (string?)null, rowVersion };
            case "workers":
                return new { name, joinDate = (DateOnly?)new DateOnly(2026, 1, 5), mainProcessId = (Guid?)null, notes = (string?)null, rowVersion };
            default:
                throw new ArgumentOutOfRangeException(nameof(route));
        }
    }

    private static string Path(string route) => $"/api/v1/master/{route}";

    private static (Guid Id, string Code, bool IsActive, uint RowVersion, string Name) Read(ApiResponse response)
    {
        using var doc = JsonDocument.Parse(response.Body);
        var root = doc.RootElement;
        return (root.GetProperty("id").GetGuid(), root.GetProperty("code").GetString()!, root.GetProperty("isActive").GetBoolean(),
            root.GetProperty("rowVersion").GetUInt32(), root.GetProperty("name").GetString()!);
    }

    private async Task<(Guid Id, string Code, uint RowVersion, string Name)> CreateAsync(string route, string? code = null, string? name = null)
    {
        var spec = Specs[route];
        var unique = name ?? "Uji " + MasterHelpers.Code("");
        var body = await BodyAsync(route, spec.CodePrefix is null ? code ?? MasterHelpers.Code("T") : null, unique);
        var created = Read((await H.Api.PostAsync(Path(route), body, H.AdminToken)).Ok($"buat {route}"));
        return (created.Id, created.Code, created.RowVersion, created.Name);
    }

    // ===== Buat, cari, lookup =====

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Buat_lalu_muncul_di_daftar_pencarian_detail_dan_lookup(string route)
    {
        var spec = Specs[route];
        var item = await CreateAsync(route);

        var detail = (await H.Api.GetAsync($"{Path(route)}/{item.Id}", H.AdminToken)).Ok();
        Assert.Equal(item.Code, Read(detail).Code);

        var list = (await H.Api.GetAsync($"{Path(route)}?q={Uri.EscapeDataString(item.Name)}", H.AdminToken)).Ok().As<PagedResult<JsonElement>>();
        Assert.Contains(list.Items, i => i.GetProperty("id").GetGuid() == item.Id);

        var lookup = (await H.Api.GetAsync($"{Path(route)}/lookup?q={Uri.EscapeDataString(item.Code)}", H.AdminToken)).Ok().As<List<LookupItem>>();
        Assert.Contains(lookup, l => l.Id == item.Id && l.Code == item.Code);

        if (spec.CodePrefix is { } prefix)
        {
            Assert.Matches($"^{prefix}-\\d{{4,}}$", item.Code);
        }
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Nonaktif_disembunyikan_dari_daftar_bawaan_dan_lookup_lalu_aktif_kembali(string route)
    {
        var item = await CreateAsync(route);

        var deactivated = Read((await H.Api.PostAsync($"{Path(route)}/{item.Id}/deactivate?rowVersion={item.RowVersion}", null, H.AdminToken)).Ok());
        Assert.False(deactivated.IsActive);

        var active = (await H.Api.GetAsync($"{Path(route)}?q={Uri.EscapeDataString(item.Name)}", H.AdminToken)).Ok().As<PagedResult<JsonElement>>();
        Assert.DoesNotContain(active.Items, i => i.GetProperty("id").GetGuid() == item.Id);

        var all = (await H.Api.GetAsync($"{Path(route)}?q={Uri.EscapeDataString(item.Name)}&status=All", H.AdminToken)).Ok().As<PagedResult<JsonElement>>();
        Assert.Contains(all.Items, i => i.GetProperty("id").GetGuid() == item.Id);

        var inactive = (await H.Api.GetAsync($"{Path(route)}?q={Uri.EscapeDataString(item.Name)}&status=Inactive", H.AdminToken)).Ok().As<PagedResult<JsonElement>>();
        Assert.Single(inactive.Items);

        var lookup = (await H.Api.GetAsync($"{Path(route)}/lookup?q={Uri.EscapeDataString(item.Code)}", H.AdminToken)).Ok().As<List<LookupItem>>();
        Assert.DoesNotContain(lookup, l => l.Id == item.Id);

        var reactivated = Read((await H.Api.PostAsync($"{Path(route)}/{item.Id}/activate?rowVersion={deactivated.RowVersion}", null, H.AdminToken)).Ok());
        Assert.True(reactivated.IsActive);
    }

    // ===== Konkurensi =====

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Ubah_dengan_rowVersion_usang_memberi_409(string route)
    {
        var spec = Specs[route];
        var item = await CreateAsync(route);
        var first = await BodyAsync(route, spec.CodePrefix is null ? item.Code : null, item.Name + " v2", item.RowVersion);
        (await H.Api.PutAsync($"{Path(route)}/{item.Id}", first, H.AdminToken)).Ok("ubah pertama");

        var stale = await BodyAsync(route, spec.CodePrefix is null ? item.Code : null, item.Name + " v3", item.RowVersion);
        var response = await H.Api.PutAsync($"{Path(route)}/{item.Id}", stale, H.AdminToken);

        ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Nonaktifkan_dengan_rowVersion_usang_memberi_409(string route)
    {
        var item = await CreateAsync(route);

        var response = await H.Api.PostAsync($"{Path(route)}/{item.Id}/deactivate?rowVersion={item.RowVersion + 9}", null, H.AdminToken);

        ProblemAssert.Is(response, HttpStatusCode.Conflict, ProblemTypes.ConcurrencyConflict);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Entitas_tidak_ada_memberi_404(string route)
    {
        ProblemAssert.Is(await H.Api.GetAsync($"{Path(route)}/{Guid.NewGuid()}", H.AdminToken), HttpStatusCode.NotFound, ProblemTypes.NotFound);
    }

    // ===== Hak akses =====

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Hak_akses_ditegakkan_di_server_per_resource_dan_aksi(string route)
    {
        var spec = Specs[route];
        var viewOnly = await H.CreateReadyUserAsync((await H.CreateRoleAsync(null, spec.View)).Id);
        var none = await H.CreateReadyUserAsync((await H.CreateRoleAsync()).Id);

        Assert.True((await H.Api.GetAsync(Path(route), viewOnly.Token)).IsSuccess, "View cukup untuk membaca");
        Assert.True((await H.Api.GetAsync($"{Path(route)}/lookup", viewOnly.Token)).IsSuccess);

        var body = await BodyAsync(route, MasterHelpers.Code("T"), "Tanpa hak");
        ProblemAssert.Is(await H.Api.PostAsync(Path(route), body, viewOnly.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync(Path(route), none.Token), HttpStatusCode.Forbidden, ProblemTypes.Forbidden);
        ProblemAssert.Is(await H.Api.GetAsync(Path(route)), HttpStatusCode.Unauthorized, ProblemTypes.SessionExpired);
    }

    // ===== Audit =====

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Pembuatan_perubahan_dan_nonaktif_tercatat_di_audit_log(string route)
    {
        var spec = Specs[route];
        var item = await CreateAsync(route);
        var updated = Read((await H.Api.PutAsync($"{Path(route)}/{item.Id}",
            await BodyAsync(route, spec.CodePrefix is null ? item.Code : null, item.Name + " ubah", item.RowVersion), H.AdminToken)).Ok());
        (await H.Api.PostAsync($"{Path(route)}/{item.Id}/deactivate?rowVersion={updated.RowVersion}", null, H.AdminToken)).Ok();

        await using var db = H.CreateDbContext();
        var events = await db.AuditLogs.Where(a => a.TargetId == item.Id.ToString()).Select(a => a.EventType).ToListAsync();
        Assert.Contains("MasterCreated", events);
        Assert.Contains("MasterUpdated", events);
        Assert.Contains("MasterDeactivated", events);
    }

    // ===== Kode manual =====

    [Theory]
    [MemberData(nameof(ManualCodeRoutes))]
    public async Task Kode_unik_tanpa_peka_huruf_besar_kecil(string route)
    {
        var item = await CreateAsync(route);
        var duplicate = await BodyAsync(route, item.Code.ToLowerInvariant(), "Duplikat");

        var problem = ProblemAssert.Is(await H.Api.PostAsync(Path(route), duplicate, H.AdminToken), HttpStatusCode.Conflict, ProblemTypes.Duplicate);

        Assert.Contains("code", problem.Errors!.Keys);
    }

    [Theory]
    [MemberData(nameof(ManualCodeRoutes))]
    public async Task Kode_wajib_berformat_benar_dan_tidak_melebihi_panjang_maksimum(string route)
    {
        var spec = Specs[route];
        var tooLong = new string('A', spec.MaxCode + 1);

        foreach (var invalid in new[] { "", "   ", "pakai spasi", "huruf#aneh", tooLong })
        {
            var response = await H.Api.PostAsync(Path(route), await BodyAsync(route, invalid, "Nama"), H.AdminToken);
            var problem = ProblemAssert.Is(response, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed);
            Assert.Contains("code", problem.Errors!.Keys);
        }
    }

    [Theory]
    [MemberData(nameof(ManualCodeRoutes))]
    public async Task Kode_dapat_diubah_selama_belum_dipakai_dan_harus_tetap_unik(string route)
    {
        var a = await CreateAsync(route);
        var b = await CreateAsync(route);
        var newCode = MasterHelpers.Code("N");

        var renamed = Read((await H.Api.PutAsync($"{Path(route)}/{a.Id}", await BodyAsync(route, newCode, a.Name, a.RowVersion), H.AdminToken))
            .Ok("ubah kode yang belum dipakai"));
        Assert.Equal(newCode, renamed.Code);

        var clash = await H.Api.PutAsync($"{Path(route)}/{a.Id}", await BodyAsync(route, b.Code.ToLowerInvariant(), a.Name, renamed.RowVersion), H.AdminToken);
        ProblemAssert.Is(clash, HttpStatusCode.Conflict, ProblemTypes.Duplicate);
    }

    // ===== Kode otomatis =====

    [Theory]
    [MemberData(nameof(AutoCodeRoutes))]
    public async Task Kode_otomatis_tidak_berubah_walau_isian_diubah(string route)
    {
        var item = await CreateAsync(route);

        var updated = Read((await H.Api.PutAsync($"{Path(route)}/{item.Id}",
            await BodyAsync(route, null, item.Name + " baru", item.RowVersion), H.AdminToken)).Ok());

        Assert.Equal(item.Code, updated.Code);
    }

    [Theory]
    [MemberData(nameof(AutoCodeRoutes))]
    public async Task Pembuatan_bersamaan_menghasilkan_kode_unik_tanpa_lubang(string route)
    {
        const int count = 12;
        var tasks = Enumerable.Range(0, count).Select(_ => CreateAsync(route)).ToArray();
        var created = await Task.WhenAll(tasks);

        var numbers = created.Select(c => int.Parse(c.Code[(c.Code.IndexOf('-') + 1)..])).OrderBy(n => n).ToList();
        Assert.Equal(count, numbers.Distinct().Count());
        Assert.Equal(count - 1, numbers[^1] - numbers[0]);
    }

    // ===== Validasi lintas entitas =====

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Nama_wajib_diisi_dan_dibatasi(string route)
    {
        var spec = Specs[route];
        var code = spec.CodePrefix is null ? MasterHelpers.Code("T") : null;

        var empty = await H.Api.PostAsync(Path(route), await BodyAsync(route, code, "   "), H.AdminToken);
        Assert.Contains("name", ProblemAssert.Is(empty, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);

        var tooLong = await H.Api.PostAsync(Path(route), await BodyAsync(route, code, new string('x', 501)), H.AdminToken);
        Assert.Contains("name", ProblemAssert.Is(tooLong, HttpStatusCode.UnprocessableEntity, ProblemTypes.ValidationFailed).Errors!.Keys);
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task Daftar_mendukung_paging_dan_urutan(string route)
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync(route);
        }

        var page = (await H.Api.GetAsync($"{Path(route)}?pageSize=2&sort=name:desc", H.AdminToken)).Ok().As<PagedResult<JsonElement>>();

        Assert.True(page.TotalCount >= 3);
        Assert.Equal(2, page.Items.Count);
        var names = page.Items.Select(i => i.GetProperty("name").GetString()!).ToList();
        Assert.True(string.Compare(names[0], names[1], StringComparison.OrdinalIgnoreCase) >= 0, "urutan nama menurun");
    }
}
