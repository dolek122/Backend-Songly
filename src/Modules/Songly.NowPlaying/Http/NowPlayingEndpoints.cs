using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Web;
using Songly.NowPlaying.Status;

namespace Songly.NowPlaying.Http;

internal static class NowPlayingEndpoints
{
    public static void MapNowPlayingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var nowPlaying = endpoints.MapGroup("/api/v1/now-playing").WithTags("Now playing");

        nowPlaying.MapGet("/me", async Task<Results<Ok<NowPlayingStatusResponse>, NoContent>> (ClaimsPrincipal user, NowPlayingService service) =>
            await service.MineAsync(user.GetUserId()) is { } status ? TypedResults.Ok(status) : TypedResults.NoContent());

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
}
