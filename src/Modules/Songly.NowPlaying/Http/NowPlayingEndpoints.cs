using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Songly.Common.Web;
using Songly.NowPlaying.Providers;
using Songly.NowPlaying.Status;

namespace Songly.NowPlaying.Http;

internal static class NowPlayingEndpoints
{
    public static void MapNowPlayingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var nowPlaying = endpoints.MapGroup("/api/v1/now-playing").WithTags("Now playing");

        nowPlaying.MapGet("/me", async Task<Results<Ok<NowPlayingStatusResponse>, NoContent>> (ClaimsPrincipal user, NowPlayingService service, CancellationToken cancellationToken) =>
            await service.MineAsync(user.GetUserId(), cancellationToken) is { } status ? TypedResults.Ok(status) : TypedResults.NoContent());

        nowPlaying.MapPost("/spotify/connect", async (ClaimsPrincipal user, ProviderConnections connections) =>
            new ConnectResponse(await connections.BeginAsync(user.GetUserId())));

        // Public: the browser comes back here from Spotify's redirect without our token; the one-time
        // state says who it is. Every result, errors included, ends in a redirect to the app (original
        // ADR 0038) — on the other side is a browser, not an API client, so a JSON error would be a dead
        // end for the user and a 204 a blank page. The only exception is a missing state: then this is
        // no return from the provider. The redirect carries no secret, so a hijacked scheme gains nothing.
        nowPlaying.MapGet("/spotify/callback", async (string? state, string? code, string? error, ProviderConnections connections, IOptions<NowPlayingOptions> options, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrEmpty(state))
            {
                throw new MissingParameterException("state");
            }

            if (string.IsNullOrEmpty(code))
            {
                // Refusing consent is a result of its own, not a failure.
                await connections.AbandonAsync(state);
                return BackToApp(options.Value, error == "access_denied" ? "denied" : "error", error == "access_denied" ? null : "MUSIC_PROVIDER_ERROR");
            }

            try
            {
                await connections.CompleteAsync(state, code, cancellationToken);
            }
            catch (ApiException exception)
            {
                return BackToApp(options.Value, "error", exception.Code);
            }

            return BackToApp(options.Value, "connected", errorCode: null);
        }).AllowAnonymous();

        nowPlaying.MapDelete("/spotify", async Task<NoContent> (ClaimsPrincipal user, ProviderConnections connections, CancellationToken cancellationToken) =>
        {
            await connections.DisconnectAsync(user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        nowPlaying.MapGet("/friends", (ClaimsPrincipal user, NowPlayingService service, CancellationToken cancellationToken) =>
            service.FriendsAsync(user.GetUserId(), cancellationToken));

        nowPlaying.MapPut("/manual", async Task<NoContent> (ManualNowPlayingRequest request, ClaimsPrincipal user, NowPlayingService service) =>
        {
            await service.SetManualAsync(user.GetUserId(), request);
            return TypedResults.NoContent();
        });

        nowPlaying.MapDelete("/manual", async Task<NoContent> (ClaimsPrincipal user, NowPlayingService service) =>
        {
            await service.ClearManualAsync(user.GetUserId());
            return TypedResults.NoContent();
        });

        nowPlaying.MapGet("/settings", (ClaimsPrincipal user, NowPlayingService service, CancellationToken cancellationToken) =>
            service.SettingsAsync(user.GetUserId(), cancellationToken));

        nowPlaying.MapPut("/settings", async Task<NoContent> (UpdateNowPlayingSettingsRequest request, ClaimsPrincipal user, NowPlayingService service, CancellationToken cancellationToken) =>
        {
            await service.UpdateSettingsAsync(user.GetUserId(), request.ShareWithFriends!.Value, cancellationToken);
            return TypedResults.NoContent();
        });
    }

    private static RedirectHttpResult BackToApp(NowPlayingOptions options, string result, string? errorCode)
    {
        var location = QueryHelpers.AddQueryString(options.ConnectResultUrl, "result", result);
        return TypedResults.Redirect(errorCode is null ? location : QueryHelpers.AddQueryString(location, "code", errorCode));
    }
}
