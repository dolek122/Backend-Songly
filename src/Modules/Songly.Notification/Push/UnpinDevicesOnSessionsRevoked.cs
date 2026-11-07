using Songly.Common.Events;
using Songly.Identity.Contracts;

namespace Songly.Notification.Push;

/// <summary>
/// Push devices go together with the sessions (original ADR 0040). Inside the revocation's transaction, and
/// not swallowing a failure: the phones are unpinned exactly when the sessions are revoked, or neither is.
/// </summary>
internal sealed class UnpinDevicesOnSessionsRevoked(PushDeviceService devices) : IDomainEventHandler<UserSessionsRevoked>
{
    public Task HandleAsync(UserSessionsRevoked domainEvent, CancellationToken cancellationToken) =>
        devices.DeleteAllForAsync(domainEvent.UserId, cancellationToken);
}
