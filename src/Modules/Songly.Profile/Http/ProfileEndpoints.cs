using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;
using Songly.Profile.Profiles;

namespace Songly.Profile.Http;

internal static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/v1/profiles/me").WithTags("Profiles");

        me.MapGet(string.Empty, (ClaimsPrincipal user, ProfileService profiles, CancellationToken cancellationToken) =>
            profiles.GetMineAsync(user.GetUserId(), cancellationToken));

        me.MapPut(string.Empty, (UpdateProfileRequest request, ClaimsPrincipal user, ProfileService profiles, CancellationToken cancellationToken) =>
            profiles.UpdateMineAsync(user.GetUserId(), request, cancellationToken));

        me.MapGet("/genres", (ClaimsPrincipal user, ProfileService profiles, CancellationToken cancellationToken) =>
            profiles.FavoriteGenresAsync(user.GetUserId(), cancellationToken));

        me.MapPut("/genres", async Task<NoContent> (SetFavoriteGenresRequest request, ClaimsPrincipal user, ProfileService profiles, CancellationToken cancellationToken) =>
        {
            await profiles.SetFavoriteGenresAsync(user.GetUserId(), request.GenreIds!, cancellationToken);
            return TypedResults.NoContent();
        });

        // Literal segments ("me", "search") take precedence over {userId}, so the routes do not collide.
        var profilesGroup = endpoints.MapGroup("/api/v1/profiles").WithTags("Profiles");

        profilesGroup.MapGet("/search", (string query, PageQuery page, ClaimsPrincipal user, UserSearch search, CancellationToken cancellationToken) =>
            search.SearchAsync(user.GetUserId(), query, page, cancellationToken));

        profilesGroup.MapGet("/{userId}", (Guid userId, ProfileService profiles, CancellationToken cancellationToken) =>
            profiles.GetPublicAsync(userId, cancellationToken));

        endpoints.MapGet("/api/v1/genres", (PageQuery page, ProfileService profiles, CancellationToken cancellationToken) =>
            profiles.ListGenresAsync(page, cancellationToken))
            .WithTags("Genres");

        // Admin-only by path (AdminAreaGuard): everything under /api/v1/admin is.
        endpoints.MapGet("/api/v1/admin/profiles", (HttpRequest request, ProfileSummaries summaries, CancellationToken cancellationToken) =>
            summaries.ForAsync(request.RequiredGuidSet("userIds"), cancellationToken))
            .WithTags("Admin");
    }
}
