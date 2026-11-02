using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;

namespace Songly.Identity;

/// <summary>Registration, login, sessions and tokens, password reset, account deletion (orchestrates erasing data in every other module).</summary>
public sealed class IdentityModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
