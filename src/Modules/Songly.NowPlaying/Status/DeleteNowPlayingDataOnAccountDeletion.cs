using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Common.Persistence;
using Songly.Identity.Contracts;
using Songly.NowPlaying.Persistence;

namespace Songly.NowPlaying.Status;

/// <summary>
/// The now-playing data of a deleted account: the rows inside the deletion's transaction, the
/// state in Redis <b>after the commit</b> and without the right to undo it (original ADR 0049).
/// </summary>
/// <remarks>
/// A Redis failure used to roll the whole account deletion back — blocking a GDPR right because of
/// a cache whose keys expire on their own anyway. Deferred work that fails is logged and leaves the
/// committed deletion alone.
/// </remarks>
internal sealed class DeleteNowPlayingDataOnAccountDeletion(NowPlayingDbContext db, StatusStore statuses, IAfterCommit afterCommit)
    : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.Preferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Links.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        afterCommit.Defer(_ => statuses.ForgetAsync(userId));
    }
}
