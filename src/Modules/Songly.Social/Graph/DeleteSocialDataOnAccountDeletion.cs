using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Identity.Contracts;
using Songly.Social.Persistence;

namespace Songly.Social.Graph;

/// <summary>Requests, friendships and blocks of a deleted account, on either side — inside the deletion's transaction.</summary>
internal sealed class DeleteSocialDataOnAccountDeletion(SocialDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.FriendRequests.Where(r => r.RequesterId == userId || r.AddresseeId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Friendships.Where(f => f.UserAId == userId || f.UserBId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Blocks.Where(x => x.BlockerId == userId || x.BlockedId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
