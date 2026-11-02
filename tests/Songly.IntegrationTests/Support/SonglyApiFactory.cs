using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Songly.Common.Modules;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Songly.IntegrationTests.SonglyApiFactory))]

namespace Songly.IntegrationTests;

/// <summary>
/// The whole application against a real Postgres in a container — never an in-memory substitute,
/// which would lie about how Postgres behaves. One instance is shared by the whole test assembly,
/// so every test creates its own accounts and never assumes an empty database.
/// </summary>
public sealed class SonglyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same image as docker-compose and the original backend's tests.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>
    /// Starts the application right away, not lazily at the first client: the migrations run at
    /// startup, and a test that seeds data with SQL before making a request would otherwise find
    /// no tables — but only when it happens to run first.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Services;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Changes data behind the application's back — for state the API cannot (yet) produce, or
    /// whose age is the subject of the test.
    /// </summary>
    public async Task<int> ExecuteSqlAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T> QuerySingleAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = Command(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Songly", ConnectionString);
        builder.ConfigureTestServices(services => services.AddSingleton<IModule, ProbeModule>());
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
