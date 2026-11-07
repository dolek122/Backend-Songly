using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Songly.Common.RateLimiting;

public static class RateLimitingExtensions
{
    /// <summary>
    /// <c>Songly:ForwardedHeaders:Enabled</c> — on only where the application runs behind a proxy that sets
    /// <c>X-Forwarded-For</c> (Render). Off elsewhere: without a proxy in front the header is whatever the client
    /// sends.
    /// </summary>
    public const string ForwardedHeadersSetting = "Songly:ForwardedHeaders:Enabled";

    public static IServiceCollection AddSonglyRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitOptions>().Bind(configuration.GetSection(RateLimitOptions.Section));
        services.AddOptions<UserRateLimitOptions>().Bind(configuration.GetSection(UserRateLimitOptions.Section));
        services.AddSingleton<FixedWindowCounter>();
        services.AddSingleton<RateLimitMetrics>();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // The proxy's own address is not known in advance on a PaaS, so the list of trusted proxies is
            // cleared — and that is safe only together with the limit of one: the middleware then takes the
            // last address in X-Forwarded-For, the one the proxy appended itself, and ignores whatever the
            // client put before it.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.ForwardLimit = 1;
        });
        return services;
    }

    /// <summary>
    /// Before authentication: the real client address (behind a proxy), then the limit per address. Placed after
    /// the request id, so a refusal is correlated like any other response.
    /// </summary>
    public static IApplicationBuilder UseSonglyRateLimiting(this IApplicationBuilder app, IConfiguration configuration)
    {
        if (configuration.GetValue<bool>(ForwardedHeadersSetting))
        {
            app.UseForwardedHeaders();
        }

        return app.UseMiddleware<ClientRateLimitingMiddleware>();
    }

    /// <summary>After authentication and authorization: the limit per user needs to know who is asking.</summary>
    public static IApplicationBuilder UseSonglyUserRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<UserRateLimitingMiddleware>();
}
