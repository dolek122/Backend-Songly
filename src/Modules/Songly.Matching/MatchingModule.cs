using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;
using Songly.Matching.Http;

namespace Songly.Matching;

/// <summary>
/// Genre-based matching between users. No data of its own — everything comes from the profile,
/// social and identity contracts — so no tables and no account-deletion handler.
/// </summary>
public sealed class MatchingModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MatchingOptions>()
            .Bind(configuration.GetSection(MatchingOptions.Section))
            .Validate(o => double.IsFinite(o.ConfidencePrior) && o.ConfidencePrior >= 0, $"{MatchingOptions.Section}: ConfidencePrior must be a non-negative number")
            .ValidateOnStart();
        services.AddScoped<MatchingService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapMatchingEndpoints();
}
