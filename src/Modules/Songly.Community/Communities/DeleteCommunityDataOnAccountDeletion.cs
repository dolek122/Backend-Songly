using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Community.Persistence;
using Songly.Identity.Contracts;

namespace Songly.Community.Communities;

/// <summary>
/// The community data of a deleted account, inside the deletion's transaction.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Bans <b>on</b> the account go: there is nobody left to keep out, and the row references <c>users</c>.</item>
/// <item>Bans imposed <b>by</b> the account stay and only lose their author. They belong to the
/// community; if they went with the moderator's account, a moderator leaving would quietly let
/// back in everyone they banned.</item>
/// <item>The memberships, and the communities the account owns — whole, with everyone else's
/// memberships and bans (<c>ON DELETE CASCADE</c> within this module).</item>
/// </list>
/// Bulk statements, which is also a condition of correctness: deleting an owned community cascades
/// into rows a per-entity delete would then try to delete a second time.
/// </remarks>
internal sealed class DeleteCommunityDataOnAccountDeletion(CommunityDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.Bans.Where(b => b.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Bans.Where(b => b.BannedBy == userId).ExecuteUpdateAsync(b => b.SetProperty(x => x.BannedBy, (Guid?)null), cancellationToken);
        await db.Members.Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Communities.Where(c => c.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
