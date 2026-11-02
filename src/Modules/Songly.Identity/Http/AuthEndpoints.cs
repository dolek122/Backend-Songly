using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Web;
using Songly.Identity.PasswordReset;
using Songly.Identity.Registration;
using Songly.Identity.Sessions;

namespace Songly.Identity.Http;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth").WithTags("Auth");

        // 202 always — also when the address already has an account. Answering differently would
        // make this endpoint a list of who has an account here. No tokens: the account does not
        // exist until the address is confirmed.
        auth.MapPost("/register", async Task<Accepted> (RegisterRequest request, RegistrationService service, CancellationToken cancellationToken) =>
            {
                await service.RegisterAsync(request, cancellationToken);
                return TypedResults.Accepted((string?)null);
            })
            .AllowAnonymous();

        auth.MapPost("/register/verify", (VerifyEmailRequest request, RegistrationService service, CancellationToken cancellationToken)
                => service.VerifyAsync(request, cancellationToken))
            .AllowAnonymous();

        auth.MapPost("/login", (LoginRequest request, AuthService service, CancellationToken cancellationToken)
                => service.LoginAsync(request, cancellationToken))
            .AllowAnonymous();

        auth.MapPost("/refresh", (RefreshRequest request, AuthService service, CancellationToken cancellationToken)
                => service.RefreshAsync(request, cancellationToken))
            .AllowAnonymous();

        // Anonymous: logging out needs the refresh token, not a still-valid access token.
        auth.MapPost("/logout", async Task<NoContent> (RefreshRequest request, AuthService service, CancellationToken cancellationToken) =>
            {
                await service.LogoutAsync(request, cancellationToken);
                return TypedResults.NoContent();
            })
            .AllowAnonymous();

        // Password reset is by definition for someone who cannot log in. 202 always — for an
        // unknown address and for a request the resend interval silenced, too.
        auth.MapPost("/password/reset-request", async Task<Accepted> (PasswordResetRequest request, PasswordResetService service, CancellationToken cancellationToken) =>
            {
                await service.RequestResetAsync(request, cancellationToken);
                return TypedResults.Accepted((string?)null);
            })
            .AllowAnonymous();

        // No token pair: a reset revokes every session, and the user logs in with the new password.
        auth.MapPost("/password/reset", async Task<NoContent> (PasswordResetConfirmRequest request, PasswordResetService service, CancellationToken cancellationToken) =>
            {
                await service.ResetPasswordAsync(request, cancellationToken);
                return TypedResults.NoContent();
            })
            .AllowAnonymous();

        // No token pair in the response, on purpose: the change revokes every session, and a fresh
        // pair here would quietly undo half of that for this device.
        auth.MapPut("/password", async Task<NoContent> (ChangePasswordRequest request, ClaimsPrincipal user, AuthService service, CancellationToken cancellationToken) =>
        {
            await service.ChangePasswordAsync(user.GetUserId(), request, cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
