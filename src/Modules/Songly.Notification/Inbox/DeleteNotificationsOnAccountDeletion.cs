using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Identity.Contracts;
using Songly.Notification.Persistence;

namespace Songly.Notification.Inbox;

/// <summary>
/// A deleted account's notifications, received and caused — both columns have a foreign key to
/// <c>users</c> — inside the deletion's transaction.
/// </summary>
internal sealed class DeleteNotificationsOnAccountDeletion(NotificationDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken) =>
        db.Notifications.Where(n => n.UserId == domainEvent.UserId || n.ActorId == domainEvent.UserId).ExecuteDeleteAsync(cancellationToken);
}
