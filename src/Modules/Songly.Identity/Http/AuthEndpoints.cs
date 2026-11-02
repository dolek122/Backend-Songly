using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Web;
using Songly.Identity.AccountDeletion;
using Songly.Identity.Administration;
using Songly.Identity.PasswordReset;
using Songly.Identity.Registration;
using Songly.Identity.Security;
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

        // Two of the few endpoints open to a suspended account: without them it could not learn
        // why, or contest the decision. An active account gets suspended=false, not a 404.
        auth.MapGet("/me/suspension", (ClaimsPrincipal user, AccountSuspensionService service, CancellationToken cancellationToken)
                => service.OwnSuspensionAsync(user.GetUserId(), cancellationToken))
            .RequireAuthorization(Policies.UserOrSuspended);

        // Open to a suspended account too: a sanction must not take away the GDPR right to erasure.
        // [FromBody] because minimal APIs do not infer a body for DELETE.
        auth.MapDelete("/me", async Task<NoContent> ([FromBody] DeleteAccountRequest request, ClaimsPrincipal user, AccountDeletionService service, CancellationToken cancellationToken) =>
            {
                await service.DeleteOwnAccountAsync(user.GetUserId(), request.Password!, cancellationToken);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.UserOrSuspended);

        auth.MapPost("/me/suspension/appeal", async Task<NoContent> (SuspensionAppealRequest request, ClaimsPrincipal user, AccountSuspensionService service, CancellationToken cancellationToken) =>
            {
                await service.AppealAsync(user.GetUserId(), request, cancellationToken);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(Policies.UserOrSuspended);

        // No token pair in the response, on purpose: the change revokes every session, and a fresh
        // pair here would quietly undo half of that for this device.
        auth.MapPut("/password", async Task<NoContent> (ChangePasswordRequest request, ClaimsPrincipal user, AuthService service, CancellationToken cancellationToken) =>
        {
            await service.ChangePasswordAsync(user.GetUserId(), request, cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
