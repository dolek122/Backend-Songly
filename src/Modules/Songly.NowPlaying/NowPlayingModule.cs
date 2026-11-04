using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Identity.Contracts;
using Songly.NowPlaying.Http;
using Songly.NowPlaying.Persistence;
using Songly.NowPlaying.Providers;
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
            .Validate(o => o.CacheTtl > TimeSpan.Zero && o.ManualStatusTtl > TimeSpan.Zero && o.OAuthStateTtl > TimeSpan.Zero, $"{NowPlayingOptions.Section}: CacheTtl, ManualStatusTtl and OAuthStateTtl must be positive")
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<NowPlayingOptions>, TokenEncryptionKeyValidator>();
        services.AddOptions<SpotifyOptions>()
            .Bind(configuration.GetSection(SpotifyOptions.Section))
            .Validate(o => o.Timeout > TimeSpan.Zero, $"{SpotifyOptions.Section}: Timeout must be positive")
            .ValidateOnStart();
        services.AddHttpClient(SpotifyClient.Name, (provider, client) => client.Timeout = provider.GetRequiredService<IOptions<SpotifyOptions>>().Value.Timeout);
        services.AddSingleton<TokenEncryptor>();
        services.AddScoped<SpotifyClient>();
        services.AddScoped<IMusicProvider, SpotifyProvider>();
        services.AddScoped<ProviderConnections>();
        services.AddSingleton<StatusStore>();
        services.AddScoped<NowPlayingService>();
        services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, DeleteNowPlayingDataOnAccountDeletion>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapNowPlayingEndpoints();
}
