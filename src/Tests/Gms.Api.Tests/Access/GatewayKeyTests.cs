using Gms.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Gms.Api.Tests.Access;

/// <summary>Kunci gerbang Web-API (API publik di PaaS): tanpa kunci benar permintaan ditolak sebelum autentikasi.</summary>
public sealed class GatewayKeyTests
{
    private static async Task<(int Status, bool Reached)> SendAsync(string? configuredKey, string path, string? suppliedKey)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configuredKey is null ? [] : new Dictionary<string, string?> { [GatewayKeyMiddleware.ConfigKey] = configuredKey }).Build();
        var reached = false;
        var middleware = new GatewayKeyMiddleware(_ => { reached = true; return Task.CompletedTask; }, config);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;
        if (suppliedKey is not null) { context.Request.Headers[GatewayKeyMiddleware.HeaderName] = suppliedKey; }

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, reached);
    }

    [Fact]
    public async Task Tanpa_kunci_terkonfigurasi_middleware_nonaktif()
    {
        var (status, reached) = await SendAsync(null, "/api/v1/users", null);
        Assert.True(reached);
        Assert.Equal(200, status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("salah")]
    [InlineData("rahasia-uat-2")]
    public async Task Kunci_hilang_atau_salah_ditolak_403_sebelum_endpoint(string? supplied)
    {
        var (status, reached) = await SendAsync("rahasia-uat", "/api/v1/users", supplied);
        Assert.False(reached);
        Assert.Equal(403, status);
    }

    [Fact]
    public async Task Kunci_benar_diteruskan()
    {
        var (status, reached) = await SendAsync("rahasia-uat", "/api/v1/users", "rahasia-uat");
        Assert.True(reached);
        Assert.Equal(200, status);
    }

    [Fact]
    public async Task Health_check_tidak_memerlukan_kunci()
    {
        var (_, reached) = await SendAsync("rahasia-uat", "/api/v1/health", null);
        Assert.True(reached);
    }
}
