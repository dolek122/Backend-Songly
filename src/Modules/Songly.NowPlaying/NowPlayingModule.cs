using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Identity.Contracts;
using Songly.NowPlaying.Http;
using Songly.NowPlaying.Persistence;
using Songly.NowPlaying.Status;

namespace Songly.NowPlaying;

/// <summary>Music provider integrations (Spotify) and the live "now playing" status.</summary>
public sealed class NowPlayingModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NowPlayingDbContext>();
        services.AddOptions<NowPlayingOptions>()
            .Bind(configuration.GetSection(NowPlayingOptions.Section))
            .Validate(o => o.CacheTtl > TimeSpan.Zero && o.ManualStatusTtl > TimeSpan.Zero, $"{NowPlayingOptions.Section}: CacheTtl and ManualStatusTtl must be positive")
            .ValidateOnStart();
        services.AddSingleton<StatusStore>();
        services.AddScoped<NowPlayingService>();
        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, DeleteNowPlayingDataOnAccountDeletion>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapNowPlayingEndpoints();
}
