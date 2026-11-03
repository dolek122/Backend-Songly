using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Community.Communities;
using Songly.Community.Contracts;
using Songly.Community.Http;
using Songly.Community.Persistence;
using Songly.Identity.Contracts;

namespace Songly.Community;

/// <summary>Topic communities, membership and moderation.</summary>
public sealed class CommunityModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CommunityDbContext>();
        services.AddScoped<CommunityService>();
        services.AddScoped<ICommunityMembership>(provider => provider.GetRequiredService<CommunityService>());
        services.AddScoped<ICommunityDirectory>(provider => provider.GetRequiredService<CommunityService>());
        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, DeleteCommunityDataOnAccountDeletion>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapCommunityEndpoints();
}
