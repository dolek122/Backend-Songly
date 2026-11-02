using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Web;
using Songly.Identity.Sessions;

namespace Songly.Identity.Http;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth").WithTags("Auth");

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

        // No token pair in the response, on purpose: the change revokes every session, and a fresh
        // pair here would quietly undo half of that for this device.
        auth.MapPut("/password", async Task<NoContent> (ChangePasswordRequest request, ClaimsPrincipal user, AuthService service, CancellationToken cancellationToken) =>
        {
            await service.ChangePasswordAsync(user.GetUserId(), request, cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
