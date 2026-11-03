using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Community.Contracts;
using Songly.Content.Persistence;
using Songly.Identity.Contracts;

namespace Songly.Content.Posts;

/// <summary>
/// The content of a deleted account, inside the deletion's transaction: first the account's
/// activity under <b>other people's</b> posts, then its own posts — which take everyone's
/// comments, likes and shares under them along (<c>ON DELETE CASCADE</c>).
/// </summary>
/// <remarks>
/// Bulk statements: no entity here carries anything that has to happen when it is deleted, and the
/// number of rows per account has no upper bound.
/// </remarks>
internal sealed class DeleteContentOnAccountDeletion(ContentDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.Comments.Where(c => c.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Likes.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Shares.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Posts.Where(p => p.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}

/// <summary>
/// The posts of a community being deleted — everyone's — inside the community deletion's
/// transaction and before its row goes: <c>posts.community_id</c> references it (see
/// <see cref="CommunityDeleted"/>).
/// </summary>
internal sealed class DeletePostsOfDeletedCommunity(ContentDbContext db) : IDomainEventHandler<CommunityDeleted>
{
    public Task HandleAsync(CommunityDeleted domainEvent, CancellationToken cancellationToken) =>
        db.Posts.Where(p => p.CommunityId == domainEvent.CommunityId).ExecuteDeleteAsync(cancellationToken);
}
