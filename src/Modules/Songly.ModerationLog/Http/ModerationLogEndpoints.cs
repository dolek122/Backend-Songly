using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;
using Songly.ModerationLog.History;

namespace Songly.ModerationLog.Http;

internal static class ModerationLogEndpoints
{
    public static void MapModerationLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/communities/{communityId}/moderation-log", (Guid communityId, PageQuery page, ClaimsPrincipal user, ModerationLogService log, CancellationToken cancellationToken) =>
            log.CommunityLogAsync(communityId, user.GetUserId(), page, cancellationToken))
            .WithTags("ModerationLog");

        // Admin-only by its path; without userId the whole platform, with it one person's history.
        endpoints.MapGet("/api/v1/admin/moderation-log", (PageQuery page, ModerationLogService log, CancellationToken cancellationToken, Guid? userId = null) =>
            log.PlatformLogAsync(userId, page, cancellationToken))
            .WithTags("ModerationLog");
    }
}
