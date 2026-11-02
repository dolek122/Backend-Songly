using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Identity.AccountDeletion;
using Songly.Identity.Administration;
using Songly.Identity.Http;
using Songly.Identity.PasswordReset;
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

        services.AddOptions<PasswordResetOptions>().Bind(configuration.GetSection(PasswordResetOptions.Section));
        services.AddScoped<PasswordResetMailer>();
        services.AddScoped<PasswordResetService>();

        services.AddOptions<AccountDeletionOptions>().Bind(configuration.GetSection(AccountDeletionOptions.Section));
        services.AddScoped<AccountEraser>();
        services.AddScoped<AccountDeletionMailer>();
        services.AddScoped<AccountDeletionService>();

        services.AddSingleton<AdministrationMetrics>();
        services.AddScoped<AccountSuspensionService>();
        services.AddScoped<AdminRoleService>();
    }

    public void UsePipeline(IApplicationBuilder app) => app.UseSonglyAuthentication();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuthEndpoints();
        endpoints.MapAdminEndpoints();
        endpoints.MapAccountDeletionPages();
    }
}
