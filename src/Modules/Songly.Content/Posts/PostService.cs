using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Common.Paging;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Content.Domain;
using Songly.Content.Http;
using Songly.Content.Images;
using Songly.Content.Persistence;
using Songly.Content.Reports;
using Songly.Social.Contracts;

namespace Songly.Content.Posts;

/// <summary>Publishing, reading and deleting posts, and the feed.</summary>
/// <remarks>
/// The original publishes <c>CommunityPostRemovedEvent</c> when a moderator deletes someone else's
/// post, with the reporters of that post; it arrives with the notification and moderation-log modules.
/// </remarks>
internal sealed class PostService(
    ContentDbContext db,
    ContentVisibility visibility,
    PostViews views,
    PostImageUploads uploads,
    PostImageFiles files,
    ReporterHistory reporterHistory,
    ICommunityMembership communities,
    ISocialGraph socialGraph,
    IDomainEvents events,
    TimeProvider time)
{
    /// <summary>
    /// <paramref name="communityId"/> <c>null</c> is an ordinary post. Publishing in a community
    /// requires membership — joining is open and immediate, so it is no barrier, but it makes writing
    /// in a community a deliberate decision. A ban removes the membership, so it blocks this too.
    /// </summary>
    /// <remarks>
    /// An image key is accepted only from the register of uploads (original ADR 0048): a key that
    /// was issued to this user and not attached yet. Using it up and saving the post are one
    /// transaction, so a failed save gives the upload back.
    /// </remarks>
    public async Task<PostResponse> CreateAsync(Guid authorId, CreatePostRequest request, Guid? communityId, CancellationToken cancellationToken)
    {
        if (communityId is { } id)
        {
            if (!await communities.ExistsAsync(id, cancellationToken))
            {
                throw new UnknownCommunityException();
            }

            if (!await communities.IsMemberAsync(id, authorId, cancellationToken))
            {
                throw new NotCommunityMemberException();
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var imageKey = await uploads.ClaimAsync(authorId, request.ImageKey, cancellationToken);
        var post = new Post(authorId, request.Content!, time.GetUtcNow(), communityId, imageKey);
        db.Posts.Add(post);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await views.ViewAsync(post, authorId, [], cancellationToken);
    }

    /// <summary>A post of a hidden author is reported as nonexistent, not 403 — see <see cref="PostNotFoundException"/>.</summary>
    public async Task<PostResponse> GetAsync(Guid postId, Guid viewerId, CancellationToken cancellationToken)
    {
        var hidden = await visibility.HiddenAuthorsForAsync(viewerId, cancellationToken);
        var post = await visibility.VisiblePostAsync(postId, hidden, cancellationToken);
        return await views.ViewAsync(post, viewerId, hidden, cancellationToken);
    }

    /// <summary>
    /// The public list, or one author's timeline — both without community posts, which belong to
    /// their community. A timeline has a single author, so one check replaces the filter: a hidden
    /// author's timeline is an empty page.
    /// </summary>
    public async Task<PagedResponse<PostResponse>> ListAsync(Guid? authorId, Guid viewerId, PageQuery page, CancellationToken cancellationToken)
    {
        var hidden = await visibility.HiddenAuthorsForAsync(viewerId, cancellationToken);
        if (authorId is { } author)
        {
            return hidden.Contains(author)
                ? PagingExtensions.Empty<PostResponse>(page)
                : await views.PageAsync(Newest(db.Posts.Where(p => p.AuthorId == author && p.CommunityId == null)), page, viewerId, hidden, cancellationToken);
        }

        return await views.PageAsync(Newest(db.Posts.Where(p => p.CommunityId == null && !hidden.Contains(p.AuthorId))), page, viewerId, hidden, cancellationToken);
    }

    /// <summary>
    /// One community's posts. Open to everyone signed in, like the community itself: hiding them
    /// from non-members would be a pretence, since joining is one request. A block is a personal
    /// boundary and holds in someone else's community too.
    /// </summary>
    public async Task<PagedResponse<PostResponse>> ListCommunityAsync(Guid communityId, Guid viewerId, PageQuery page, CancellationToken cancellationToken)
    {
        if (!await communities.ExistsAsync(communityId, cancellationToken))
        {
            throw new UnknownCommunityException();
        }

        var hidden = await visibility.HiddenAuthorsForAsync(viewerId, cancellationToken);
        return await views.PageAsync(Newest(db.Posts.Where(p => p.CommunityId == communityId && !hidden.Contains(p.AuthorId))), page, viewerId, hidden, cancellationToken);
    }

    /// <summary>
    /// The posts written or shared by the viewer's friends and the viewer, newest post first. A query
    /// over the list of users is enough for hundreds of friends; with real traffic it is the first
    /// candidate for a read replica or a precomputed feed (original Claude.md §10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CommunityId == null</c> is a condition on the <b>post</b>, not on the share: otherwise
    /// sharing a community post would smuggle it into the feed of people who never chose that
    /// community.
    /// </para>
    /// <para>
    /// The hidden authors are a condition of their own, separate from the list of users. A block
    /// ends the friendship, which takes the blocked user out of the list as an author — but their
    /// post still came in <b>shared by a mutual friend</b>. That was the only way the blocked
    /// user's content returned to the blocker's eyes.
    /// </para>
    /// </remarks>
    public async Task<PagedResponse<PostResponse>> FeedAsync(Guid viewerId, PageQuery page, CancellationToken cancellationToken)
    {
        var users = new HashSet<Guid>(await socialGraph.FriendIdsAsync(viewerId, cancellationToken)) { viewerId };
        var hidden = await visibility.HiddenAuthorsForAsync(viewerId, cancellationToken);
        var feed = db.Posts.Where(p =>
            p.CommunityId == null
            && !hidden.Contains(p.AuthorId)
            && (users.Contains(p.AuthorId) || db.Shares.Any(s => s.PostId == p.Id && users.Contains(s.UserId))));
        return await views.PageAsync(Newest(feed), page, viewerId, hidden, cancellationToken);
    }

    /// <summary>
    /// The author deletes their posts anywhere; the community's owner and moderators delete any post
    /// <b>in their community</b> (original ADR 0011) — without that a community has no defence
    /// against spam. It does not spill over onto ordinary posts: there only authorship counts.
    /// Comments, likes and shares go with the post (<c>ON DELETE CASCADE</c>); its image goes after
    /// the save — without that, every deleted post would leave a file nothing points to and no row
    /// could ever lead to.
    /// </summary>
    public async Task DeleteAsync(Guid postId, Guid requesterId, CancellationToken cancellationToken)
    {
        var post = await db.Posts.SingleOrDefaultAsync(p => p.Id == postId, cancellationToken) ?? throw new PostNotFoundException();
        if (post.AuthorId != requesterId && !await ModeratesAsync(post, requesterId, cancellationToken))
        {
            throw new NotResourceOwnerException();
        }

        // Deleted by moderation (not by its author — that decides nobody's report): every report of the
        // post turns out to be right (original ADR 0027). The reporters are read before the delete —
        // the cascade takes the reports along, and afterwards there is nothing to read.
        CommunityPostRemoved? removal = null;
        if (post.AuthorId != requesterId && post.CommunityId is { } communityId)
        {
            var reporterIds = await db.PostReports.Where(r => r.PostId == postId).Select(r => r.ReporterId).Distinct().ToListAsync(cancellationToken);
            foreach (var reporterId in reporterIds)
            {
                reporterHistory.Record(reporterId, communityId, Domain.ReporterDecisionOutcome.Upheld);
            }

            removal = new CommunityPostRemoved(communityId, post.AuthorId, requesterId, reporterIds.ToHashSet(), time.GetUtcNow());
        }

        // One save: the decisions and the deletion commit together.
        db.Posts.Remove(post);
        await db.SaveChangesAsync(cancellationToken);
        if (post.ImageKey is { } imageKey)
        {
            await files.DeleteOrphanedAsync(imageKey, cancellationToken);
        }

        if (removal is not null)
        {
            await events.PublishAsync(removal, cancellationToken);
        }
    }

    /// <summary>Moderation reaches posts in communities only — under an ordinary post, only authorship counts.</summary>
    public async Task<bool> ModeratesAsync(Post post, Guid requesterId, CancellationToken cancellationToken) =>
        post.CommunityId is { } communityId && await communities.CanModerateAsync(communityId, requesterId, cancellationToken);

    /// <summary>Newest first; the id breaks ties, so that pages do not overlap (ADR 0009).</summary>
    private static IOrderedQueryable<Post> Newest(IQueryable<Post> posts) =>
        posts.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id);
}
