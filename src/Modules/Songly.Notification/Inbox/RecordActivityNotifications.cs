using Songly.Common.Events;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Notification.Domain;
using Songly.Social.Contracts;

namespace Songly.Notification.Inbox;

/// <summary>
/// Other people's activity, turned into notifications. The module's only contact with the rest of the
/// system: events published by content, social and community after their save (original Claude.md
/// §2) — no reaching for their entities or services.
/// </summary>
internal sealed class RecordActivityNotifications(NotificationService inbox)
    : IDomainEventHandler<PostLiked>,
      IDomainEventHandler<PostCommented>,
      IDomainEventHandler<PostShared>,
      IDomainEventHandler<FriendRequestSent>,
      IDomainEventHandler<FriendRequestAccepted>,
      IDomainEventHandler<CommunityMemberJoined>,
      IDomainEventHandler<CommunityMemberRoleChanged>
{
    public Task HandleAsync(PostLiked domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.PostAuthorId, domainEvent.LikerId, NotificationType.PostLiked, domainEvent.PostId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(PostCommented domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.PostAuthorId, domainEvent.CommentAuthorId, NotificationType.PostCommented, domainEvent.PostId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(PostShared domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.PostAuthorId, domainEvent.SharerId, NotificationType.PostShared, domainEvent.PostId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(FriendRequestSent domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.AddresseeId, domainEvent.RequesterId, NotificationType.FriendRequestReceived, domainEvent.RequestId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(FriendRequestAccepted domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.RequesterId, domainEvent.AddresseeId, NotificationType.FriendRequestAccepted, domainEvent.RequestId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityMemberJoined domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.OwnerId, domainEvent.MemberId, NotificationType.CommunityMemberJoined, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    /// <summary>
    /// An ordinary notification, not a moderation one (original ADR 0016): the role grants or takes away
    /// rights but restricts nothing in ordinary participation, so it is under the block filter like any
    /// activity, and it will not go out by e-mail.
    /// </summary>
    public Task HandleAsync(CommunityMemberRoleChanged domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(
            domainEvent.UserId,
            domainEvent.ChangedBy,
            domainEvent.ModerationGranted ? NotificationType.CommunityModeratorGranted : NotificationType.CommunityModeratorRevoked,
            domainEvent.CommunityId,
            domainEvent.OccurredAt,
            cancellationToken);
}
