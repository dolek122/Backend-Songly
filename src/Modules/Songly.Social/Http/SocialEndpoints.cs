using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;
using Songly.Social.Graph;

namespace Songly.Social.Http;

internal static class SocialEndpoints
{
    public static void MapSocialEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var friends = endpoints.MapGroup("/api/v1/friends").WithTags("Friends");

        friends.MapPost("/requests", async Task<Created<FriendRequestResponse>> (SendFriendRequestRequest request, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
            TypedResults.Created((string?)null, await social.SendRequestAsync(user.GetUserId(), request.AddresseeId!.Value, cancellationToken)));

        friends.MapGet("/requests", (string? direction, PageQuery page, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
            social.ListRequestsAsync(user.GetUserId(), ParseDirection(direction), page, cancellationToken));

        friends.MapPost("/requests/{requestId}/accept", (Guid requestId, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
            social.AcceptRequestAsync(requestId, user.GetUserId(), cancellationToken));

        friends.MapPost("/requests/{requestId}/decline", (Guid requestId, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
            social.DeclineRequestAsync(requestId, user.GetUserId(), cancellationToken));

        friends.MapDelete("/requests/{requestId}", async Task<NoContent> (Guid requestId, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
        {
            await social.CancelRequestAsync(requestId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        friends.MapGet(string.Empty, (PageQuery page, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
            social.ListFriendsAsync(user.GetUserId(), page, cancellationToken));

        friends.MapDelete("/{userId}", async Task<NoContent> (Guid userId, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
        {
            await social.RemoveFriendAsync(user.GetUserId(), userId, cancellationToken);
            return TypedResults.NoContent();
        });

        var blocks = endpoints.MapGroup("/api/v1/blocks").WithTags("Blocks");

        blocks.MapPut("/{userId}", async Task<NoContent> (Guid userId, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
        {
            await social.BlockAsync(user.GetUserId(), userId, cancellationToken);
            return TypedResults.NoContent();
        });

        // The block state for someone's profile screen (original ADR 0039). Here, not in the profile:
        // profile cannot depend on social — social depends on profile.
        blocks.MapGet("/{userId}", async (Guid userId, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
            new BlockStatusResponse(await social.IsBlockingAsync(user.GetUserId(), userId, cancellationToken)));

        blocks.MapDelete("/{userId}", async Task<NoContent> (Guid userId, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
        {
            await social.UnblockAsync(user.GetUserId(), userId, cancellationToken);
            return TypedResults.NoContent();
        });

        blocks.MapGet(string.Empty, (PageQuery page, ClaimsPrincipal user, SocialService social, CancellationToken cancellationToken) =>
            social.ListBlockedAsync(user.GetUserId(), page, cancellationToken));
    }

    /// <summary>
    /// The exact names, as Spring bound an enum: case-sensitive, no numbers. Minimal APIs would accept
    /// "incoming" and "0" too.
    /// </summary>
    private static FriendRequestDirection ParseDirection(string? direction) => direction switch
    {
        null => FriendRequestDirection.Incoming,
        "INCOMING" => FriendRequestDirection.Incoming,
        "OUTGOING" => FriendRequestDirection.Outgoing,
        _ => throw new InvalidParameterException("direction"),
    };
}
