using System.Net;
using System.Text;
using Gms.Shared.Access;
using Gms.Shared.Problems;
using Gms.Web.Infrastructure.Auth;
using Gms.Web.Infrastructure.Http;
using Gms.Web.Services.Api;

namespace Gms.Web.Tests;

/// <summary>Klien HTTP ke API: token, parsing Problem Details, kegagalan jaringan, dan data tiruan yang meniru perilaku API.</summary>
public sealed class ApiClientTests
{
    private sealed class FixedToken(string? token) : ISessionTokenProvider
    {
        public string? Token { get; private set; } = token;

        public ValueTask<string?> GetTokenAsync() => ValueTask.FromResult(Token);

        public void Override(string? value) => Token = value;
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    private static GmsApiClient Client(Handler handler, string? token = "tok") => new(new Factory(handler), new FixedToken(token));

    private static HttpResponseMessage Json(HttpStatusCode status, string json, string media = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, media) };

    // ===== Permintaan =====

    [Fact]
    public async Task Token_sesi_dilampirkan_sebagai_Bearer_dan_tidak_dikirim_bila_kosong()
    {
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"nilai\":5}")));

        await Client(handler, "abc123").GetAsync<Dictionary<string, int>>("/api/v1/x");
        await Client(handler, token: null).GetAsync<Dictionary<string, int>>("/api/v1/x");
        await Client(handler, token: "").GetAsync<Dictionary<string, int>>("/api/v1/x");

        Assert.Equal(("Bearer", "abc123"), (handler.Requests[0].Headers.Authorization!.Scheme, handler.Requests[0].Headers.Authorization!.Parameter));
        Assert.Null(handler.Requests[1].Headers.Authorization);
        Assert.Null(handler.Requests[2].Headers.Authorization);
    }

    [Fact]
    public async Task Badan_JSON_memakai_camelCase_dan_enum_string_dan_null_tidak_ditulis()
    {
        string? sent = null;
        var handler = new Handler(async r => { sent = await r.Content!.ReadAsStringAsync(); return Json(HttpStatusCode.OK, "{}"); });

        await Client(handler).PostAsync("/api/v1/x", new Gms.Shared.CustomerPo.PoListQuery { Status = Gms.Shared.Enums.PoStatus.Confirmed, Q = null });

        Assert.Contains("\"status\":\"Confirmed\"", sent);
        Assert.DoesNotContain("\"q\"", sent);
        Assert.Contains("\"page\":1", sent);
    }

    [Fact]
    public async Task Respons_204_dan_kosong_dikembalikan_tanpa_galat_dan_JSON_dibaca_bertipe()
    {
        var handler = new Handler(r => Task.FromResult(r.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : Json(HttpStatusCode.OK, "{\"user\":{\"id\":\"" + Guid.Empty + "\",\"userName\":\"a\",\"fullName\":\"A\",\"roleId\":\"" + Guid.Empty + "\",\"roleName\":\"R\",\"isAdministrator\":true},\"mustChangePassword\":false,\"permissions\":[\"X.View\"]}")));

        var me = await Client(handler).GetAsync<MeResponse>("/api/v1/auth/me");

        Assert.Equal(("a", true, 1), (me.User.UserName, me.User.IsAdministrator, me.Permissions.Count));
        await Client(handler).PostAsync("/api/v1/auth/logout");
    }

    [Fact]
    public async Task Unduhan_membawa_nama_berkas_tipe_dan_isi_biner()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46]) };
        response.Content.Headers.ContentType = new("application/pdf");
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = "\"SJ-202611-0001.pdf\"" };
        var handler = new Handler(_ => Task.FromResult(response));

        var file = await Client(handler).DownloadAsync("/api/v1/deliveries/x/pdf");

        Assert.Equal(("SJ-202611-0001.pdf", "application/pdf", 4), (file.FileName, file.ContentType, file.Bytes.Length));
    }

    // ===== Galat =====

    [Fact]
    public async Task Problem_JSON_diurai_lengkap_dengan_code_violations_dan_traceId()
    {
        const string problem = """
            {"type":"urn:gms:problem:rule-violation","title":"Aturan bisnis dilanggar.","status":409,"detail":"Qty melebihi batas.",
             "code":"allocation.exceeded","traceId":"00-abc","violations":[{"code":"allocation.exceeded","subject":"Navy / M","current":21,"limit":20,"message":"21 > 20"}]}
            """;
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.Conflict, problem, "application/problem+json")));

        var ex = await Assert.ThrowsAsync<ApiException>(() => Client(handler).PostAsync<object>("/api/v1/x", new { }));

        Assert.Equal((409, ProblemTypes.RuleViolation, "allocation.exceeded", "00-abc"), (ex.StatusCode, ex.Problem.Type, ex.Problem.Code, ex.Problem.TraceId));
        var violation = Assert.Single(ex.Problem.Violations!);
        Assert.Equal((21m, 20m), (violation.Current!.Value, violation.Limit!.Value));
    }

    [Fact]
    public async Task Galat_validasi_422_membawa_kamus_errors_per_field()
    {
        const string problem = """{"type":"urn:gms:problem:validation-failed","status":422,"errors":{"lines[0].qty":["Qty harus > 0"],"customerId":["Wajib"]}}""";
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.UnprocessableEntity, problem, "application/problem+json")));

        var ex = await Assert.ThrowsAsync<ApiException>(() => Client(handler).GetAsync<object>("/x"));

        Assert.Equal(["Qty harus > 0"], ex.Problem.Errors!["lines[0].qty"]);
        Assert.Equal(2, ex.Problem.Errors.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProblemKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ProblemKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ProblemKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ProblemKind.Server)]
    [InlineData(HttpStatusCode.BadGateway, ProblemKind.Server)]
    public async Task Badan_bukan_JSON_dipetakan_menurut_status_tanpa_membocorkan_isi(HttpStatusCode status, ProblemKind expected)
    {
        var handler = new Handler(_ => Task.FromResult(Json(status, "<html>Stack trace rahasia</html>", "text/html")));

        var ex = await Assert.ThrowsAsync<ApiException>(() => Client(handler).GetAsync<object>("/x"));

        Assert.Equal(expected, ProblemPresenter.Classify(ex.Problem));
        Assert.DoesNotContain("rahasia", ex.Message + ex.Problem.Detail + ex.Problem.Title);
    }

    [Fact]
    public async Task JSON_rusak_pada_respons_galat_jatuh_ke_pemetaan_status()
    {
        var handler = new Handler(_ => Task.FromResult(Json(HttpStatusCode.InternalServerError, "{ bukan json", "application/problem+json")));

        var ex = await Assert.ThrowsAsync<ApiException>(() => Client(handler).GetAsync<object>("/x"));

        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task Kegagalan_jaringan_dan_timeout_menjadi_503_manusiawi()
    {
        var down = new Handler(_ => throw new HttpRequestException("connection refused"));
        var slow = new Handler(_ => throw new TaskCanceledException("timeout"));

        var a = await Assert.ThrowsAsync<ApiException>(() => Client(down).GetAsync<object>("/x"));
        var b = await Assert.ThrowsAsync<ApiException>(() => Client(slow).GetAsync<object>("/x"));

        Assert.All(new[] { a, b }, ex =>
        {
            Assert.Equal((503, ProblemTypes.ServiceUnavailable), (ex.StatusCode, ex.Problem.Type));
            Assert.DoesNotContain("refused", ex.Message);
        });
    }

    [Fact]
    public async Task Pembatalan_oleh_pemanggil_tidak_diubah_menjadi_galat_jaringan()
    {
        using var cts = new CancellationTokenSource();
        var handler = new Handler(_ => { cts.Cancel(); throw new TaskCanceledException(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).GetAsync<object>("/x", cts.Token));
    }

    // ===== Data tiruan meniru API =====

    private static MockAuthApi Mock(string? token = null) => new(new FixedToken(token));

    [Fact]
    public async Task Mock_login_benar_menghasilkan_token_pengguna_dan_permission_dan_salah_melempar_ApiException_seperti_API()
    {
        var api = Mock();

        var ok = await api.LoginAsync(new LoginRequest("admin", "Admin123!"));
        Assert.True(ok.User.IsAdministrator);
        Assert.NotEmpty(ok.Token);
        Assert.True(ok.Permissions.Count > 20);

        var wrong = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync(new LoginRequest("admin", "salah")));
        Assert.Equal((401, ProblemTypes.InvalidCredentials), (wrong.StatusCode, wrong.Problem.Type));
        Assert.NotNull(wrong.Problem.TraceId);

        var unknown = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync(new LoginRequest("tidak-ada", "x")));
        Assert.Equal(wrong.Problem.Detail, unknown.Problem.Detail); // tidak membocorkan keberadaan username
    }

    [Fact]
    public async Task Mock_mengunci_akun_setelah_lima_kali_gagal_dan_login_baru_mencabut_sesi_lama()
    {
        var api = Mock();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync(new LoginRequest("kunci", "salah")));
        }

        var locked = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync(new LoginRequest("kunci", "salah")));
        Assert.Equal(ProblemTypes.AccountLocked, locked.Problem.Type);
        Assert.NotNull(locked.Problem.LockedUntil);

        var first = await api.LoginAsync(new LoginRequest("staf", "Staf123!"));
        var second = await api.LoginAsync(new LoginRequest("staf", "Staf123!"));
        var stale = await Assert.ThrowsAsync<ApiException>(() => Mock(first.Token).GetMeAsync());
        Assert.Equal(ProblemTypes.SessionExpired, stale.Problem.Type);
        Assert.Equal("staf", (await Mock(second.Token).GetMeAsync()).User.UserName);
    }

    [Fact]
    public async Task Mock_ganti_password_menolak_password_lama_salah_dan_menggugurkan_wajib_ganti()
    {
        var login = await Mock().LoginAsync(new LoginRequest("baru", "Baru123!"));
        Assert.True(login.MustChangePassword);
        var api = Mock(login.Token);

        var wrong = await Assert.ThrowsAsync<ApiException>(() => api.ChangePasswordAsync(new ChangePasswordRequest("salah", "Rahasia123")));
        Assert.Equal(ProblemTypes.ValidationFailed, wrong.Problem.Type);

        await api.ChangePasswordAsync(new ChangePasswordRequest("Baru123!", "Rahasia123"));
        Assert.False((await api.GetMeAsync()).MustChangePassword);
        await api.LogoutAsync();
        await Assert.ThrowsAsync<ApiException>(() => api.GetMeAsync());
    }
}
