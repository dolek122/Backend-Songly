using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;

namespace Songly.ModerationLog;

/// <summary>Append-only history of moderation decisions.</summary>
public sealed class ModerationLogModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
