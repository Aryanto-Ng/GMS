namespace Gms.E2E.Tests;

/// <summary>Uji kontrak langsung ke API nyata sesuai docs/Architecture/ApiConventions.md (tanpa UI).</summary>
[Collection(StackCollection.Name)]
public sealed class ContractE2ETests(StackFixture stack)
{
    [Theory]
    [InlineData("active")]
    [InlineData("inactive")]
    [InlineData("all")]
    public async Task Filter_status_master_menerima_nilai_huruf_kecil_sesuai_kontrak(string status)
    {
        var (code, body) = await stack.Seed.GetRawAsync($"/api/v1/master/uoms?status={status}");

        Assert.True(code == 200, $"GET /master/uoms?status={status} → {code}: {body}");
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("All")]
    public async Task Filter_status_master_juga_menerima_nama_enum_berhuruf_besar(string status)
    {
        var (code, body) = await stack.Seed.GetRawAsync($"/api/v1/master/uoms?status={status}");

        Assert.True(code == 200, $"GET /master/uoms?status={status} → {code}: {body}");
    }
}

/// <summary>Parameter query yang dikirim klien web (HttpXxxApi) harus diterima API nyata.</summary>
[Collection(StackCollection.Name)]
public sealed class QueryContractE2ETests(StackFixture stack)
{
    [Theory]
    [InlineData("/api/v1/purchase-orders?status=Confirmed&terlambat=true&jatuhTempo7=true&poCustomerKosong=true&sisaRencana=true&page=1&pageSize=25&q=abc")]
    [InlineData("/api/v1/purchase-orders?from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/v1/work-orders?status=Released&terlambat=true&menungguClose=true")]
    [InlineData("/api/v1/production/entries?kind=Output&status=Active&hariIni=true&includeVoided=true")]
    [InlineData("/api/v1/deliveries?status=Voided&q=SJ")]
    [InlineData("/api/v1/handovers/fg?status=Active")]
    [InlineData("/api/v1/handovers/material?status=Active")]
    [InlineData("/api/v1/stock?hanyaAdaStok=true&q=x")]
    [InlineData("/api/v1/users?status=Locked&q=a")]
    [InlineData("/api/v1/audit-logs?from=2026-11-01&to=2026-11-10&eventType=LoginFailed")]
    [InlineData("/api/v1/master/fgs?status=Active&readyOnly=true")]
    [InlineData("/api/v1/master/uoms/lookup?q=a")]
    [InlineData("/api/v1/deliveries/eligible-pos?q=a")]
    [InlineData("/api/v1/payroll/periods")]
    [InlineData("/api/v1/payroll/periods/next-defaults")]
    [InlineData("/api/v1/permissions")]
    public async Task Parameter_query_klien_web_diterima_API(string path)
    {
        var (code, body) = await stack.Seed.GetRawAsync(path);

        Assert.True(code == 200, $"GET {path} → {code}: {body}");
    }
}
