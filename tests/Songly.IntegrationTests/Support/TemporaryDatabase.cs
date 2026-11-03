using Npgsql;

namespace Songly.IntegrationTests;

/// <summary>
/// A database of its own on the shared Postgres container, dropped afterwards — for a test whose
/// data must not reach the shared database, where every other test would see it. Pass
/// <see cref="Settings"/> to <see cref="SonglyApiFactory.WithSettings"/>: the instance migrates it
/// at startup.
/// </summary>
public sealed class TemporaryDatabase : IAsyncDisposable
{
    private readonly SonglyApiFactory _factory;
    private readonly string _name;

    private TemporaryDatabase(SonglyApiFactory factory, string name)
    {
        _factory = factory;
        _name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { Database = name }.ConnectionString;
    }

    public string ConnectionString { get; }

    public (string Key, string? Value)[] Settings =>
    [
        ("ConnectionStrings:Songly", ConnectionString),
        ("Database:MigrateOnStartup", "true"),
    ];

    public static async Task<TemporaryDatabase> CreateAsync(SonglyApiFactory factory)
    {
        var name = $"songly_{Guid.NewGuid():N}";
        await factory.ExecuteSqlAsync($"CREATE DATABASE {name}");
        return new TemporaryDatabase(factory, name);
    }

    public async Task<T> QuerySingleAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = Command(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Changes data behind the application's back, like <see cref="SonglyApiFactory.ExecuteSqlAsync"/>.</summary>
    public async Task ExecuteSqlAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
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

    public async ValueTask DisposeAsync()
    {
        // The application's pooled connections may still be open; FORCE closes them.
        await _factory.ExecuteSqlAsync($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
    }
}
