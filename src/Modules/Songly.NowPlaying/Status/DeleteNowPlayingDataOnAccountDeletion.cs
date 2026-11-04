using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Common.Persistence;
using Songly.Identity.Contracts;
using Songly.NowPlaying.Persistence;
using Songly.NowPlaying.Stream;

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
internal sealed class DeleteNowPlayingDataOnAccountDeletion(NowPlayingDbContext db, StatusStore statuses, StatusPublisher publisher, IAfterCommit afterCommit)
    : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.Preferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Links.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        afterCommit.Defer(async _ =>
        {
            await statuses.ForgetAsync(userId);

            // Without it, friends with an open stream would see the status of an account that no longer
            // exists, until their own reconnect. After the commit: not before a deletion that could still roll back.
            await publisher.PublishStoppedAsync(userId);
        });
    }
}
