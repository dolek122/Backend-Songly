using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Songly.Api;

/// <summary>Healthy only when Postgres answers a query, not merely when the process is up.</summary>
internal sealed class DatabaseHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("Database unreachable", exception);
        }
    }
}
