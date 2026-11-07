using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Songly.Common.Persistence;

public static class DatabaseExtensions
{
    public const string ConnectionStringName = "Songly";

    /// <summary>
    /// The one Postgres connection pool of the application. Every module's <see cref="DbContext"/>
    /// draws from it, so there is a single place that decides how the app talks to the database.
    /// </summary>
    public static IServiceCollection AddSonglyDatabase(this IServiceCollection services)
    {
        services.AddSingleton(provider => NpgsqlDataSource.Create(ConnectionString(provider.GetRequiredService<IConfiguration>())));

        // One per request: the handlers of a request defer into the transaction that request runs.
        services.AddScoped<ModuleTransaction>();
        services.AddScoped<IAfterCommit>(provider => provider.GetRequiredService<ModuleTransaction>());
        return services;
    }

    /// <summary>
    /// Registers a module's own <see cref="DbContext"/>. Each module maps only its own tables
    /// (original Claude.md §2: no module reaches into another one's data), onto the schema the
    /// migrations created — so names follow the schema's snake_case, not the C# property names.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>())
            .UseSnakeCaseNamingConvention());
        return services;
    }

    /// <summary>In Npgsql's <c>Key=Value;</c> form, or a <c>postgresql://</c> URL (<see cref="PostgresUrl"/>).</summary>
    public static string ConnectionString(IConfiguration configuration) =>
        PostgresUrl.ToConnectionString(
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured"));
}
