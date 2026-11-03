using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;
using Songly.Content.Posts;

namespace Songly.Content.Http;

internal static class ContentEndpoints
{
    public static void MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var posts = endpoints.MapGroup("/api/v1/posts").WithTags("Posts");

        posts.MapPost(string.Empty, async Task<Created<PostResponse>> (CreatePostRequest request, ClaimsPrincipal user, PostService service, CancellationToken cancellationToken) =>
            TypedResults.Created((string?)null, await service.CreateAsync(user.GetUserId(), request, communityId: null, cancellationToken)));

        posts.MapGet(string.Empty, (Guid? authorId, PageQuery page, ClaimsPrincipal user, PostService service, CancellationToken cancellationToken) =>
            service.ListAsync(authorId, user.GetUserId(), page, cancellationToken));

        posts.MapGet("/{postId}", (Guid postId, ClaimsPrincipal user, PostService service, CancellationToken cancellationToken) =>
            service.GetAsync(postId, user.GetUserId(), cancellationToken));

        posts.MapDelete("/{postId}", async Task<NoContent> (Guid postId, ClaimsPrincipal user, PostService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(postId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        posts.MapPut("/{postId}/likes", async Task<NoContent> (Guid postId, ClaimsPrincipal user, ReactionService service, CancellationToken cancellationToken) =>
        {
            await service.LikeAsync(postId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        posts.MapDelete("/{postId}/likes", async Task<NoContent> (Guid postId, ClaimsPrincipal user, ReactionService service, CancellationToken cancellationToken) =>
        {
            await service.UnlikeAsync(postId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        posts.MapPut("/{postId}/shares", async Task<NoContent> (Guid postId, ClaimsPrincipal user, ReactionService service, CancellationToken cancellationToken) =>
        {
            await service.ShareAsync(postId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        posts.MapDelete("/{postId}/shares", async Task<NoContent> (Guid postId, ClaimsPrincipal user, ReactionService service, CancellationToken cancellationToken) =>
        {
            await service.UnshareAsync(postId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        posts.MapPost("/{postId}/comments", async Task<Created<CommentResponse>> (Guid postId, CreateCommentRequest request, ClaimsPrincipal user, CommentService service, CancellationToken cancellationToken) =>
            TypedResults.Created((string?)null, await service.AddAsync(postId, user.GetUserId(), request, cancellationToken)));

        posts.MapGet("/{postId}/comments", (Guid postId, PageQuery page, ClaimsPrincipal user, CommentService service, CancellationToken cancellationToken) =>
            service.ListAsync(postId, user.GetUserId(), page, cancellationToken));

        posts.MapDelete("/{postId}/comments/{commentId}", async Task<NoContent> (Guid postId, Guid commentId, ClaimsPrincipal user, CommentService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(postId, commentId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        endpoints.MapGet("/api/v1/feed", (PageQuery page, ClaimsPrincipal user, PostService service, CancellationToken cancellationToken) =>
            service.FeedAsync(user.GetUserId(), page, cancellationToken)).WithTags("Feed");

        // The path belongs to the community, the resource is a post — so it lives here: the community
        // module knows nothing about posts. Everything else about such a post (likes, comments,
        // deleting) goes through /api/v1/posts/{postId}: it is the same post, published elsewhere.
        var communityPosts = endpoints.MapGroup("/api/v1/communities/{communityId}/posts").WithTags("Community posts");

        communityPosts.MapPost(string.Empty, async Task<Created<PostResponse>> (Guid communityId, CreatePostRequest request, ClaimsPrincipal user, PostService service, CancellationToken cancellationToken) =>
            TypedResults.Created((string?)null, await service.CreateAsync(user.GetUserId(), request, communityId, cancellationToken)));

        communityPosts.MapGet(string.Empty, (Guid communityId, PageQuery page, ClaimsPrincipal user, PostService service, CancellationToken cancellationToken) =>
            service.ListCommunityAsync(communityId, user.GetUserId(), page, cancellationToken));
    }
}
