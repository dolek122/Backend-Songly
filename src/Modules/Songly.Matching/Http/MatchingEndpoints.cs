using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;

namespace Songly.Matching.Http;

internal static class MatchingEndpoints
{
    public static void MapMatchingEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/matches", (PageQuery page, ClaimsPrincipal user, MatchingService matching, CancellationToken cancellationToken) =>
            matching.MatchesAsync(user.GetUserId(), page, cancellationToken))
            .WithTags("Matching");
}
