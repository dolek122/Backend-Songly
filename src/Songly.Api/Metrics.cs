using OpenTelemetry.Metrics;
using Songly.Identity.Contracts;

namespace Songly.Api;

/// <summary>
/// The metrics the application records, exported for Prometheus — the original's
/// <c>/actuator/prometheus</c> (original Claude.md §13).
/// </summary>
/// <remarks>
/// The meters themselves are plain <c>System.Diagnostics.Metrics</c>, created by the modules through
/// <c>IMeterFactory</c>; none of them knows about OpenTelemetry. This is the only place that does: it picks
/// which meters leave the process and where to.
/// </remarks>
internal static class Metrics
{
    public const string Path = "/metrics";

    public static IServiceCollection AddSonglyMetrics(this IServiceCollection services)
    {
        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            // Every meter of the application — they are all named "Songly.<Module>[.<Area>]".
            .AddMeter("Songly.*")

            // Latency and status per route and the server's connections, which ASP.NET Core records on its
            // own (the original's http.server.requests from Spring Boot) — not duplicated in our meters.
            .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel")

            // GC, thread pool, memory: the runtime's own meter.
            .AddMeter("System.Runtime")
            .AddPrometheusExporter());
        return services;
    }

    /// <summary>
    /// For administrators only, as in the original: the metrics say how many logins fail, how many tokens are
    /// reused and what the rate limiters refuse — a map for whoever is attacking.
    /// </summary>
    public static void MapSonglyMetrics(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPrometheusScrapingEndpoint(Path).RequireAuthorization(AuthorizationPolicies.Admin);
}
