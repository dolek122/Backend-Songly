using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

        // Public: communities are open, so is the member list (the ban list is not).
        communities.MapGet("/{communityId}/members", (Guid communityId, PageQuery page, CommunityService service, CancellationToken cancellationToken) =>
            service.ListMembersAsync(communityId, page, cancellationToken));

        communities.MapPut("/{communityId}/members/{userId}/role", async Task<NoContent> (Guid communityId, Guid userId, SetMemberRoleRequest request, ClaimsPrincipal user, CommunityService service, CancellationToken cancellationToken) =>
        {
            await service.SetMemberRoleAsync(communityId, user.GetUserId(), userId, request.Role!.Value, cancellationToken);
            return TypedResults.NoContent();
        });

        // The body is optional: a ban without a reason and without a term is a valid decision.
        communities.MapPut("/{communityId}/bans/{userId}", async Task<NoContent> (Guid communityId, Guid userId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BanMemberRequest? request, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
        {
            await bans.BanAsync(communityId, user.GetUserId(), userId, request, cancellationToken);
            return TypedResults.NoContent();
        });

        // Lifting a ban is also accepting a pending appeal — one decision, one endpoint.
        communities.MapDelete("/{communityId}/bans/{userId}", async Task<NoContent> (Guid communityId, Guid userId, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
        {
            await bans.UnbanAsync(communityId, user.GetUserId(), userId, cancellationToken);
            return TypedResults.NoContent();
        });

        communities.MapGet("/{communityId}/bans", (Guid communityId, PageQuery page, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
            bans.ListAsync(communityId, user.GetUserId(), page, cancellationToken));

        communities.MapPost("/{communityId}/ban-appeal", async Task<NoContent> (Guid communityId, BanAppealRequest request, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
        {
            await bans.AppealAsync(communityId, user.GetUserId(), request.Text!, cancellationToken);
            return TypedResults.NoContent();
        });

        communities.MapPost("/{communityId}/ban-appeal/escalation", async Task<NoContent> (Guid communityId, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
        {
            await bans.EscalateAppealAsync(communityId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        communities.MapPost("/{communityId}/bans/{userId}/appeal/rejection", async Task<NoContent> (Guid communityId, Guid userId, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
        {
            await bans.RejectAppealAsync(communityId, user.GetUserId(), userId, cancellationToken);
            return TypedResults.NoContent();
        });

        // The second instance (original ADR 0028). Admin-only by path — everything under /api/v1/admin is.
        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Admin");

        admin.MapGet("/ban-appeals", (BanService bans, CancellationToken cancellationToken) => bans.EscalatedAppealsAsync(cancellationToken));

        // Overturning the ban is upholding the appeal at the second instance.
        admin.MapDelete("/communities/{communityId}/bans/{userId}", async Task<NoContent> (Guid communityId, Guid userId, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
        {
            await bans.OverturnAsync(communityId, userId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        admin.MapPost("/communities/{communityId}/bans/{userId}/escalation-rejection", async Task<NoContent> (Guid communityId, Guid userId, ClaimsPrincipal user, BanService bans, CancellationToken cancellationToken) =>
        {
            await bans.RejectEscalationAsync(communityId, userId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
