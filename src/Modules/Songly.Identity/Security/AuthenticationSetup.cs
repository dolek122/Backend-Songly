using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Songly.Common.Web;

namespace Songly.Identity.Security;

internal static class AuthenticationSetup
{
    public static IServiceCollection AddSonglyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddSingleton<AccessTokens>();
        services.AddScoped<AccountStatusService>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                // Claims keep their JWT names ("sub"), not the legacy XML-schema URIs.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = AccessTokens.ValidationParameters(jwt.Value);
                bearer.Events = new JwtBearerEvents { OnTokenValidated = AddRolesFromLiveAccountState };
            });

        services.AddAuthorizationBuilder()
            // Every endpoint requires a full, non-suspended account unless it explicitly says
            // otherwise (AllowAnonymous or a named policy). A new endpoint is closed by default,
            // also to suspended accounts — it cannot be forgotten the way a check could.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireRole(Roles.User).Build())
            .AddPolicy(Policies.UserOrSuspended, policy => policy.RequireRole(Roles.User, Roles.Suspended));

        return services;
    }

    public static IApplicationBuilder UseSonglyAuthentication(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseMiddleware<AuthenticatedRequestLogging>();
        app.UseMiddleware<AdminAreaGuard>();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// The token proves who is asking; what they may do comes from the database, on every request.
    /// </summary>
    private static async Task AddRolesFromLiveAccountState(TokenValidatedContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identity || !context.Principal.TryGetUserId(out var userId))
        {
            context.Fail("The token has no valid subject");
            return;
        }

        var status = await context.HttpContext.RequestServices.GetRequiredService<AccountStatusService>()
            .StatusOfAsync(userId, context.HttpContext.RequestAborted);

        foreach (var role in RolesOf(status))
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }
    }

    private static IEnumerable<string> RolesOf(AccountStatus status)
    {
        if (status.IsSuspended)
        {
            // Instead of USER, never next to it — not even for a suspended administrator.
            yield return Roles.Suspended;
            yield break;
        }

        yield return Roles.User;
        if (status.IsAdmin)
        {
            yield return Roles.Admin;
        }
    }
}
