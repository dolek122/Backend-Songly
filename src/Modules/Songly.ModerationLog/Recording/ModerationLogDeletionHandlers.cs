using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Community.Contracts;
using Songly.Identity.Contracts;
using Songly.ModerationLog.Persistence;

namespace Songly.ModerationLog.Recording;

/// <summary>
/// Inside the deletion's transaction — both <c>subject_id</c> and <c>actor_id</c> have a foreign key to
/// <c>users</c>. The entries <b>about</b> the person go; the entries <b>by</b> them stay without an author, like
/// <c>banned_by</c> (original ADR 0011): a decision belongs to the community, not to the person who made it, and
/// a history of sanctions against an account that no longer exists has nothing to describe.
/// </summary>
/// <remarks>
/// A known cost (original ADR 0031): deleting the account of a freshly appointed administrator takes the entries
/// about their appointment with it. Keeping them would need an id without an owner — exactly what the GDPR path
/// must not leave behind.
/// </remarks>
internal sealed class EraseModerationLogOnAccountDeletion(ModerationLogDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.Entries.Where(e => e.SubjectId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Entries.Where(e => e.ActorId == userId).ExecuteUpdateAsync(set => set.SetProperty(e => e.ActorId, (Guid?)null), cancellationToken);
    }
}

/// <summary>
/// A deleted community's history goes with it, inside the deletion's transaction — <c>community_id</c> has a
/// foreign key to <c>communities</c>. Deleted, not set to <see langword="null"/>: an entry without a community
/// would read as a platform decision, which it never was.
/// </summary>
internal sealed class EraseModerationLogOfDeletedCommunity(ModerationLogDbContext db) : IDomainEventHandler<CommunityDeleted>
{
    public Task HandleAsync(CommunityDeleted domainEvent, CancellationToken cancellationToken) =>
        db.Entries.Where(e => e.CommunityId == domainEvent.CommunityId).ExecuteDeleteAsync(cancellationToken);
}
