using Microsoft.EntityFrameworkCore;
using Songly.Common.Paging;
using Songly.Common.Storage;
using Songly.Content.Domain;
using Songly.Content.Http;
using Songly.Content.Persistence;
using Songly.Profile.Contracts;

namespace Songly.Content.Posts;

/// <summary>
/// Turns posts into <see cref="PostResponse"/>s: counters, "liked/shared by me", the author's name
/// and avatar — for a whole page in a fixed number of queries, never one per post.
/// </summary>
internal sealed class PostViews(ContentDbContext db, IProfileDirectory profiles, IObjectStorage storage)
{
    public async Task<PagedResponse<PostResponse>> PageAsync(IQueryable<Post> ordered, PageQuery page, Guid viewerId, HashSet<Guid> hiddenAuthors, CancellationToken cancellationToken)
    {
        var posts = await ordered.ToPageAsync(page, post => post, cancellationToken);
        return new PagedResponse<PostResponse>(await ViewsAsync(posts.Content, viewerId, hiddenAuthors, cancellationToken), posts.Page);
    }

    public async Task<PostResponse> ViewAsync(Post post, Guid viewerId, HashSet<Guid> hiddenAuthors, CancellationToken cancellationToken) =>
        (await ViewsAsync([post], viewerId, hiddenAuthors, cancellationToken))[0];

    /// <summary>
    /// The comment counter leaves out the same authors as the comment list — otherwise a post
    /// would say "5 comments" above a list of four. Likes and shares are not filtered on purpose:
    /// they are pure numbers without identity, so nobody can read from them that a blocked user
    /// was here, or answer them (original Claude.md §9).
    /// </summary>
    public async Task<IReadOnlyList<PostResponse>> ViewsAsync(IReadOnlyList<Post> posts, Guid viewerId, HashSet<Guid> hiddenAuthors, CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var postIds = posts.Select(p => p.Id).ToList();
        var likes = await db.Likes.Where(l => postIds.Contains(l.PostId))
            .GroupBy(l => l.PostId).Select(g => new { g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var comments = await db.Comments.Where(c => postIds.Contains(c.PostId) && !hiddenAuthors.Contains(c.AuthorId))
            .GroupBy(c => c.PostId).Select(g => new { g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var shares = await db.Shares.Where(s => postIds.Contains(s.PostId))
            .GroupBy(s => s.PostId).Select(g => new { g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var likedByViewer = (await db.Likes.Where(l => postIds.Contains(l.PostId) && l.UserId == viewerId).Select(l => l.PostId).ToListAsync(cancellationToken)).ToHashSet();
        var sharedByViewer = (await db.Shares.Where(s => postIds.Contains(s.PostId) && s.UserId == viewerId).Select(s => s.PostId).ToListAsync(cancellationToken)).ToHashSet();

        var authorIds = posts.Select(p => p.AuthorId).Distinct().ToList();
        var names = await profiles.DisplayNamesAsync(authorIds, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(authorIds, cancellationToken);

        return posts.Select(post => new PostResponse(
            post.Id,
            post.AuthorId,
            names.GetValueOrDefault(post.AuthorId),
            avatars.GetValueOrDefault(post.AuthorId),
            post.Content,
            post.ImageKey is null ? null : storage.PublicUrl(post.ImageKey),
            post.CreatedAt,
            post.CommunityId,
            likes.GetValueOrDefault(post.Id),
            likedByViewer.Contains(post.Id),
            comments.GetValueOrDefault(post.Id),
            shares.GetValueOrDefault(post.Id),
            sharedByViewer.Contains(post.Id))).ToList();
    }
}
