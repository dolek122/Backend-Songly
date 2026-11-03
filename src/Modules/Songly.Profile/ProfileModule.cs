using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Identity.Contracts;
using Songly.Profile.Contracts;
using Songly.Profile.Http;
using Songly.Profile.Persistence;
using Songly.Profile.Profiles;

namespace Songly.Profile;

/// <summary>Public profile data, favourite genres, finding people by name.</summary>
public sealed class ProfileModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ProfileDbContext>();
        services.AddSingleton<AvatarFiles>();
        services.AddOptions<GenrePopularityOptions>().Bind(configuration.GetSection(GenrePopularityOptions.Section));
        services.AddSingleton<GenrePopularityCache>();
        services.AddScoped<IProfileDirectory, ProfileDirectory>();
        services.AddScoped<ProfileService>();
        services.AddScoped<UserSearch>();
        services.AddScoped<ProfileSummaries>();
        services.AddScoped<IDomainEventHandler<UserRegistered>, CreateProfileOnRegistration>();
        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, DeleteProfileOnAccountDeletion>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapProfileEndpoints();
}
