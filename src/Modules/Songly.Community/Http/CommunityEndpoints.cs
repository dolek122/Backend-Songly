using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;
using Songly.Community.Communities;

namespace Songly.Community.Http;

internal static class CommunityEndpoints
{
    public static void MapCommunityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var communities = endpoints.MapGroup("/api/v1/communities").WithTags("Communities");

        communities.MapPost(string.Empty, async Task<Created<CommunityResponse>> (CreateCommunityRequest request, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
            TypedResults.Created((string?)null, await service.CreateAsync(user.GetUserId(), request, cancellationToken)));

        communities.MapGet(string.Empty, (PageQuery page, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
            service.ListAsync(user.GetUserId(), page, cancellationToken));

        // A literal segment takes precedence over {communityId}.
        communities.MapGet("/mine", (PageQuery page, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
            service.ListMineAsync(user.GetUserId(), page, cancellationToken));

        communities.MapGet("/{communityId}", (Guid communityId, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
            service.GetAsync(communityId, user.GetUserId(), cancellationToken));

        communities.MapPut("/{communityId}", (Guid communityId, UpdateCommunityRequest request, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
            service.UpdateAsync(communityId, user.GetUserId(), request, cancellationToken));

        communities.MapDelete("/{communityId}", async Task<NoContent> (Guid communityId, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(communityId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        communities.MapPut("/{communityId}/membership", async Task<NoContent> (Guid communityId, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
        {
            await service.JoinAsync(communityId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        communities.MapGet("/{communityId}/membership", (Guid communityId, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
            service.MembershipStatusAsync(communityId, user.GetUserId(), cancellationToken));

        communities.MapDelete("/{communityId}/membership", async Task<NoContent> (Guid communityId, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
        {
            await service.LeaveAsync(communityId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        // Public: communities are open, so is the member list (bans are not — they come with moderation).
        communities.MapGet("/{communityId}/members", (Guid communityId, PageQuery page, CommunityService service, CancellationToken cancellationToken) =>
            service.ListMembersAsync(communityId, page, cancellationToken));

        communities.MapPut("/{communityId}/members/{userId}/role", async Task<NoContent> (Guid communityId, Guid userId, SetMemberRoleRequest request, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
        {
            await service.SetMemberRoleAsync(communityId, user.GetUserId(), userId, request.Role!.Value, cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
