using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Identity.Http;
using Songly.Identity.Persistence;
using Songly.Identity.Registration;
using Songly.Identity.Security;
using Songly.Identity.Sessions;

namespace Songly.Identity;

/// <summary>
/// Registration, login, sessions and tokens, password reset, account deletion (orchestrates
/// erasing data in every other module).
/// </summary>
public sealed class IdentityModule : IModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityDbContext>();
        services.AddSonglyAuthentication(configuration);
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<IdentityMetrics>();
        services.AddScoped<TokenIssuer>();
        services.AddScoped<AuthService>();

        services.AddOptions<RegistrationOptions>().Bind(configuration.GetSection(RegistrationOptions.Section));
        services.AddScoped<RegistrationMailer>();
        services.AddScoped<RegistrationService>();
        services.AddHostedService<PendingRegistrationCleanupJob>();
    }

    public void UsePipeline(IApplicationBuilder app) => app.UseSonglyAuthentication();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAuthEndpoints();
}
