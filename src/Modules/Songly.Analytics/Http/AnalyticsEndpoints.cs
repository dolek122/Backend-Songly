using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Songly.Analytics.Counting;
using Songly.Identity.Contracts;

namespace Songly.Analytics.Http;

internal static class AnalyticsEndpoints
{
    /// <summary>Admin-only, like in the original — outside <c>/api/v1/admin</c>, so by the named policy rather than the path.</summary>
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/analytics/summary", (SummaryService summaries, CancellationToken cancellationToken, int days = 7) =>
            summaries.SummaryAsync(days, cancellationToken))
            .RequireAuthorization(AuthorizationPolicies.Admin)
            .WithTags("Analytics");
}
