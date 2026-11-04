using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Common.Persistence;
using Songly.Community.Contracts;
using Songly.Content.Images;
using Songly.Content.Persistence;
using Songly.Identity.Contracts;

namespace Songly.Content.Posts;

/// <summary>
/// The content of a deleted account, inside the deletion's transaction: first the account's
/// activity under <b>other people's</b> posts, then its pending uploads and its own posts — which
/// take everyone's comments, likes and shares under them along (<c>ON DELETE CASCADE</c>).
/// </summary>
/// <remarks>
/// Bulk statements: no entity here carries anything that has to happen when it is deleted, and the
/// number of rows per account has no upper bound. The image keys are read <b>before</b> the rows go
/// — afterwards there is nowhere to read them from — and the files are deleted after the commit.
/// </remarks>
internal sealed class DeleteContentOnAccountDeletion(ContentDbContext db, PostImageFiles files, IAfterCommit afterCommit)
    : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.Comments.Where(c => c.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Likes.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Shares.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Reports filed by the account go — no reporter, no report — and so does its track record: a new
        // account is a new identity, keeping it would protect against nothing. Reports it decided as a
        // moderator stay and lose only their author: a moderator leaving must not undo their decisions.
        await db.PostReports.Where(r => r.ReporterId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.ReporterDecisions.Where(d => d.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.PostReports.Where(r => r.ReviewedBy == userId).ExecuteUpdateAsync(r => r.SetProperty(x => x.ReviewedBy, (Guid?)null), cancellationToken);

        var imageKeys = await db.PostImageUploads.Where(u => u.OwnerId == userId).Select(u => u.ImageKey).ToListAsync(cancellationToken);
        imageKeys.AddRange(await db.Posts.Where(p => p.AuthorId == userId && p.ImageKey != null).Select(p => p.ImageKey!).ToListAsync(cancellationToken));
        await db.PostImageUploads.Where(u => u.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Posts.Where(p => p.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);

        foreach (var key in imageKeys)
        {
            afterCommit.Defer(token => files.DeleteOrphanedAsync(key, token));
        }
    }
}

/// <summary>
/// The posts of a community being deleted — everyone's — inside the community deletion's
/// transaction and before its row goes: <c>posts.community_id</c> references it (see
/// <see cref="CommunityDeleted"/>). Their reports go with them (cascade), the decisions made in
/// the community explicitly (they reference it), and the images after the commit.
/// </summary>
internal sealed class DeletePostsOfDeletedCommunity(ContentDbContext db, PostImageFiles files, IAfterCommit afterCommit)
    : IDomainEventHandler<CommunityDeleted>
{
    public async Task HandleAsync(CommunityDeleted domainEvent, CancellationToken cancellationToken)
    {
        var communityId = domainEvent.CommunityId;
        var imageKeys = await db.Posts.Where(p => p.CommunityId == communityId && p.ImageKey != null).Select(p => p.ImageKey!).ToListAsync(cancellationToken);
        await db.Posts.Where(p => p.CommunityId == communityId).ExecuteDeleteAsync(cancellationToken);
        await db.ReporterDecisions.Where(d => d.CommunityId == communityId).ExecuteDeleteAsync(cancellationToken);

        foreach (var key in imageKeys)
        {
            afterCommit.Defer(token => files.DeleteOrphanedAsync(key, token));
        }
    }
}
