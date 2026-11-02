using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;

namespace Songly.Content;

/// <summary>Posts, likes, comments, shares and the feed.</summary>
public sealed class ContentModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
