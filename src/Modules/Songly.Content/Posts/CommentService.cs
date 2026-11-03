using Microsoft.EntityFrameworkCore;
using Songly.Common.Paging;
using Songly.Community.Contracts;
using Songly.Content.Domain;
using Songly.Content.Http;
using Songly.Content.Persistence;
using Songly.Profile.Contracts;

namespace Songly.Content.Posts;

/// <summary>Comments under posts.</summary>
/// <remarks>
/// The original publishes <c>PostCommentedEvent</c> for every comment and
/// <c>CommunityCommentRemovedEvent</c> when a moderator removes someone else's comment; they arrive
/// with their consumers (notification, analytics, moderation log).
/// </remarks>
internal sealed class CommentService(
    ContentDbContext db,
    ContentVisibility visibility,
    PostService posts,
    ICommunityMembership communities,
    IProfileDirectory profiles,
    TimeProvider time)
{
    /// <summary>
    /// A ban cuts off writing in a community, and a comment is writing. Removing the membership did
    /// not catch it — commenting needs only a sign-in, so the banned user would keep reaching people,
    /// which is the very path bans exist for. Likes and shares stay: pure numbers without identity
    /// or text (the same line as with blocks, original Claude.md §9).
    /// </summary>
    public async Task<CommentResponse> AddAsync(Guid postId, Guid authorId, CreateCommentRequest request, CancellationToken cancellationToken)
    {
        var post = await visibility.VisiblePostAsync(postId, authorId, cancellationToken);
        if (post.CommunityId is { } communityId && await communities.IsBannedAsync(communityId, authorId, cancellationToken))
        {
            throw new BannedFromCommunityException();
        }

        var comment = new Comment(postId, authorId, request.Content!, time.GetUtcNow());
        db.Comments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);
        return (await ViewsAsync([comment], cancellationToken))[0];
    }

    /// <summary>
    /// Oldest first, like a conversation. Comments of hidden authors are filtered in the query: under
    /// someone else's post the two sides of a block meet without any relation between them, and this
    /// is the most direct channel of contact there is.
    /// </summary>
    public async Task<PagedResponse<CommentResponse>> ListAsync(Guid postId, Guid viewerId, PageQuery page, CancellationToken cancellationToken)
    {
        var hidden = await visibility.HiddenAuthorsForAsync(viewerId, cancellationToken);
        await visibility.VisiblePostAsync(postId, hidden, cancellationToken);
        var comments = await db.Comments.AsNoTracking()
            .Where(c => c.PostId == postId && !hidden.Contains(c.AuthorId))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToPageAsync(page, comment => comment, cancellationToken);
        return new PagedResponse<CommentResponse>(await ViewsAsync(comments.Content, cancellationToken), comments.Page);
    }

    /// <summary>
    /// The author deletes their comment anywhere; the community's owner and moderators delete any
    /// comment under a post <b>in their community</b> (original ADR 0045). Before that the only answer
    /// to a troll's comment was deleting the whole post — punishing its author for someone else's
    /// words. Under an ordinary post nothing changes: the post's author does not moderate comments.
    /// </summary>
    public async Task DeleteAsync(Guid postId, Guid commentId, Guid requesterId, CancellationToken cancellationToken)
    {
        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId && c.PostId == postId, cancellationToken)
            ?? throw new CommentNotFoundException();
        if (comment.AuthorId != requesterId)
        {
            var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == postId, cancellationToken) ?? throw new CommentNotFoundException();
            if (!await posts.ModeratesAsync(post, requesterId, cancellationToken))
            {
                throw new NotResourceOwnerException();
            }
        }

        db.Comments.Remove(comment);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CommentResponse>> ViewsAsync(IReadOnlyList<Comment> comments, CancellationToken cancellationToken)
    {
        var authorIds = comments.Select(c => c.AuthorId).Distinct().ToList();
        var names = await profiles.DisplayNamesAsync(authorIds, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(authorIds, cancellationToken);
        return comments
            .Select(c => new CommentResponse(c.Id, c.PostId, c.AuthorId, names.GetValueOrDefault(c.AuthorId), avatars.GetValueOrDefault(c.AuthorId), c.Content, c.CreatedAt))
            .ToList();
    }
}
