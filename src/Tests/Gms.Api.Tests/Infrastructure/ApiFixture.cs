using Npgsql;
using Testcontainers.PostgreSql;

namespace Gms.Api.Tests.Infrastructure;

/// <summary>
/// Satu container PostgreSQL 17 untuk seluruh tes (dibuang di akhir). <see cref="Main"/> adalah API bersama;
/// tes yang membutuhkan database kosong memakai <see cref="CreateIsolatedAsync"/>.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private readonly List<TestHost> _isolated = [];

    public TestHost Main { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();
        Main = await TestHost.StartAsync(_container.GetConnectionString());
    }

    /// <summary>Database baru yang kosong di container yang sama, sudah dimigrasi dan di-seed.</summary>
    public async Task<TestHost> CreateIsolatedAsync()
    {
        var name = "iso_" + Guid.NewGuid().ToString("N")[..12];
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {name}";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name };
        var host = await TestHost.StartAsync(builder.ConnectionString);
        _isolated.Add(host);
        return host;
    }

    public string ServerConnectionString => _container.GetConnectionString();

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _isolated)
        {
            await host.DisposeAsync();
        }

        await Main.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
