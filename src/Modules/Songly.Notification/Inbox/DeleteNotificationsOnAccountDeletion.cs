using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Identity.Contracts;
using Songly.Notification.Persistence;

namespace Songly.Notification.Inbox;

/// <summary>
/// A deleted account's notifications, received and caused — both columns have a foreign key to
/// <c>users</c> — and its mail preferences, inside the deletion's transaction.
/// </summary>
internal sealed class DeleteNotificationsOnAccountDeletion(NotificationDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var userId = domainEvent.UserId;
        await db.Notifications.Where(n => n.UserId == userId || n.ActorId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.MailPreferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
