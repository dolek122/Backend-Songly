using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Identity.Contracts;
using Songly.Social.Contracts;
using Songly.Social.Graph;
using Songly.Social.Http;
using Songly.Social.Persistence;

namespace Songly.Social;

/// <summary>Friends, friend requests and blocking.</summary>
public sealed class SocialModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SocialDbContext>();
        services.AddScoped<SocialService>();
        services.AddScoped<ISocialGraph>(provider => provider.GetRequiredService<SocialService>());
        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, DeleteSocialDataOnAccountDeletion>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapSocialEndpoints();
}
