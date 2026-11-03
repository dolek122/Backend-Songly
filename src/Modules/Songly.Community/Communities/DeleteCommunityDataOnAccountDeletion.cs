using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Community.Persistence;
using Songly.Identity.Contracts;

namespace Songly.Community.Communities;

/// <summary>
/// The memberships of a deleted account, and the communities it owns — whole, with everyone else's
/// memberships (<c>ON DELETE CASCADE</c> within this module). Inside the deletion's transaction.
/// </summary>
internal sealed class DeleteCommunityDataOnAccountDeletion(CommunityDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        await db.Members.Where(m => m.UserId == domainEvent.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Communities.Where(c => c.OwnerId == domainEvent.UserId).ExecuteDeleteAsync(cancellationToken);
    }
}
