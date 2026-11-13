using Gms.Api.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Gms.Api.Tests.Hosting;

/// <summary>BUG-002: pool Npgsql dibatasi agar lonjakan permintaan tidak menghabiskan slot server (SqlState 53300).</summary>
public sealed class ConnectionPoolTests
{
    private static IConfiguration Config(string? connection, int? maxPool = null)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection };
        if (maxPool is not null)
        {
            values["Database:MaxPoolSize"] = maxPool.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Tanpa_pengaturan_pool_dibatasi_ke_nilai_bawaan()
    {
        var result = ApiHost.WithPoolLimit(Config("Host=localhost;Port=5432;Database=gms;Username=gms;Password=x"));
        Assert.Equal(ApiHost.DefaultMaxPoolSize, new NpgsqlConnectionStringBuilder(result).MaxPoolSize);
    }

    [Fact]
    public void Konfigurasi_Database_MaxPoolSize_dipakai()
    {
        var result = ApiHost.WithPoolLimit(Config("Host=localhost;Database=gms;Username=gms", 7));
        Assert.Equal(7, new NpgsqlConnectionStringBuilder(result).MaxPoolSize);
    }

    [Fact]
    public void Nilai_pada_connection_string_tidak_ditimpa()
    {
        var result = ApiHost.WithPoolLimit(Config("Host=localhost;Database=gms;Username=gms;Maximum Pool Size=55", 7));
        Assert.Equal(55, new NpgsqlConnectionStringBuilder(result).MaxPoolSize);
    }

    [Fact]
    public void Connection_string_kosong_dikembalikan_apa_adanya()
    {
        Assert.Null(ApiHost.WithPoolLimit(Config(null)));
    }
}
