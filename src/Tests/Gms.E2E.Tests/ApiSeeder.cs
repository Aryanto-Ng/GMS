using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gms.Shared.Access;
using Gms.Shared.Common;
using Gms.Shared.CustomerPo;
using Gms.Shared.Enums;
using Gms.Shared.Master;
using Gms.Shared.WorkOrders;

namespace Gms.E2E.Tests;

/// <summary>Id data dasar yang disiapkan lewat API (bukan UI) agar E2E fokus pada alur pengguna.</summary>
public sealed record BaseData(
    Guid CustomerId, Guid ColorId, Guid SizeSId, Guid SizeMId, Guid CuttingId, Guid SewingId, Guid WorkerId, Guid FgId, string FgCode, string CustomerName);

/// <summary>Klien API sebagai Administrator untuk menyiapkan/memeriksa data. Tidak dipakai untuk menguji UI.</summary>
public sealed class ApiSeeder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly HttpClient _http;

    private ApiSeeder(HttpClient http) => _http = http;

    public static async Task<ApiSeeder> CreateAsync(string apiUrl, string bootstrapPassword, string finalPassword)
    {
        var http = new HttpClient { BaseAddress = new Uri(apiUrl) };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin", bootstrapPassword), Json);
        await EnsureAsync(login, "login admin bootstrap");
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(Json))!.Token;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await EnsureAsync(await http.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(bootstrapPassword, finalPassword), Json), "ganti password admin");
        return new ApiSeeder(http);
    }

    private static async Task EnsureAsync(HttpResponseMessage response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{what}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    public async Task<T> PostAsync<T>(string path, object? body = null)
    {
        var response = await _http.PostAsJsonAsync(path, body, Json);
        await EnsureAsync(response, $"POST {path}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task PostAsync(string path, object? body = null) => await EnsureAsync(await _http.PostAsJsonAsync(path, body, Json), $"POST {path}");

    public async Task<T> PutAsync<T>(string path, object body)
    {
        var response = await _http.PutAsJsonAsync(path, body, Json);
        await EnsureAsync(response, $"PUT {path}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<T> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(path);
        await EnsureAsync(response, $"GET {path}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    /// <summary>GET mentah: status HTTP dan isi (untuk uji kontrak yang mengharapkan galat).</summary>
    public async Task<(int Status, string Body)> GetRawAsync(string path)
    {
        var response = await _http.GetAsync(path);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();

    // ----- Pengguna -----

    /// <summary>Pengguna baru (wajib ganti password saat login pertama).</summary>
    public async Task<(string UserName, string Password)> CreateUserAsync(string roleName = "Administrator")
    {
        var roles = await GetAsync<List<RoleListItem>>("/api/v1/roles");
        var role = roles.First(r => r.Name == roleName);
        var name = Unique("u").ToLowerInvariant();
        await PostAsync<UserDetail>("/api/v1/users", new CreateUserRequest(name, "Pengguna Uji " + name, role.Id, "Awal-12345"));
        return (name, "Awal-12345");
    }

    /// <summary>Pengguna siap pakai (password sudah diganti) — setiap tes UI memakai pengguna sendiri karena API hanya mengizinkan satu sesi aktif per pengguna.</summary>
    public async Task<(string UserName, string Password)> CreateReadyUserAsync(string roleName = "Administrator")
    {
        var (user, initial) = await CreateUserAsync(roleName);
        var final = "Siap-" + Guid.NewGuid().ToString("N")[..8];
        using var http = new HttpClient { BaseAddress = _http.BaseAddress };
        var login = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(user, initial), Json);
        await EnsureAsync(login, "login pengguna siap pakai");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>(Json))!.Token);
        await EnsureAsync(await http.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(initial, final), Json), "ganti password pengguna siap pakai");
        return (user, final);
    }

    // ----- Data dasar -----

    public async Task<BaseData> CreateBaseDataAsync()
    {
        var customer = await PostAsync<CustomerDto>("/api/v1/master/customers", new SaveCustomerRequest(Unique("PT Uji "), "Bu Uji", "0812", null, null));
        var color = await PostAsync<ColorDto>("/api/v1/master/colors", new SaveColorRequest(Unique("C"), "Navy " + Guid.NewGuid().ToString("N")[..4], "#1F2A5A"));
        var sizeS = await PostAsync<SizeDto>("/api/v1/master/sizes", new SaveSizeRequest(Unique("S"), "Small", 1));
        var sizeM = await PostAsync<SizeDto>("/api/v1/master/sizes", new SaveSizeRequest(Unique("M"), "Medium", 2));
        var cutting = await PostAsync<ProcessDto>("/api/v1/master/processes", new SaveProcessRequest(Unique("CUT"), "Cutting " + Guid.NewGuid().ToString("N")[..3], ProcessType.Internal));
        var sewing = await PostAsync<ProcessDto>("/api/v1/master/processes", new SaveProcessRequest(Unique("SEW"), "Sewing " + Guid.NewGuid().ToString("N")[..3], ProcessType.Internal));
        var worker = await PostAsync<WorkerDto>("/api/v1/master/workers", new SaveWorkerRequest("Pekerja " + Guid.NewGuid().ToString("N")[..4], new DateOnly(2026, 1, 5), cutting.Id, null));

        var fgCode = Unique("FG-");
        var fg = await PostAsync<FgDto>("/api/v1/master/fgs", new SaveFgRequest(fgCode, "Kemeja Uji", "Kemeja", customer.Id, null));
        var sizes = await PutAsync<FgSizesDto>($"/api/v1/master/fgs/{fg.Id}/sizes", new SaveFgSizesRequest([sizeS.Id, sizeM.Id], false, fg.RowVersion));
        var routing = await PutAsync<FgRoutingDto>($"/api/v1/master/fgs/{fg.Id}/routing", new SaveRoutingRequest([cutting.Id, sewing.Id], false, sizes.RowVersion));
        var cells = new List<TariffCell>();
        foreach (var size in new[] { sizeS.Id, sizeM.Id })
        {
            cells.Add(new TariffCell(cutting.Id, size, 350m));
            cells.Add(new TariffCell(sewing.Id, size, 2500m));
        }

        await PutAsync<FgTariffGridDto>($"/api/v1/master/fgs/{fg.Id}/tariffs", new SaveTariffsRequest(cells, routing.RowVersion));
        return new BaseData(customer.Id, color.Id, sizeS.Id, sizeM.Id, cutting.Id, sewing.Id, worker.Id, fg.Id, fgCode, customer.Name);
    }

    /// <summary>PO Confirmed untuk data dasar: S dan M masing-masing 100 / 200 pcs.</summary>
    public async Task<PoDto> CreateConfirmedPoAsync(BaseData data, string? customerPoNumber = null)
    {
        var delivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        return await PostAsync<PoDto>("/api/v1/purchase-orders", new SavePoRequest(
            data.CustomerId, DateOnly.FromDateTime(DateTime.UtcNow), customerPoNumber, null,
            [new SavePoLine(null, data.FgId, data.ColorId, data.SizeSId, delivery, 100, null), new SavePoLine(null, data.FgId, data.ColorId, data.SizeMId, delivery, 200, null)],
            ConfirmAfterSave: true));
    }

    public async Task<WoDto> CreateReleasedWoAsync(PoDto po, int sQty = 60, int mQty = 100)
    {
        var lines = po.Lines.Select(l => new SaveWoLine(l.Id, l.ColorSize.SizeCode.StartsWith('S') ? sQty : mQty)).ToList();
        var draft = await PostAsync<WoDto>("/api/v1/work-orders", new SaveWoRequest(po.Id, po.Lines[0].FgId, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), null, lines));
        return await PostAsync<WoDto>($"/api/v1/work-orders/{draft.Id}/release", new WoActionRequest(null, draft.RowVersion));
    }
}
