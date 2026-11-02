using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;

namespace Songly.Analytics;

/// <summary>Aggregated, PII-free counters of domain events.</summary>
public sealed class AnalyticsModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
