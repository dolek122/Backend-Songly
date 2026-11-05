using Songly.Common.Events;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Notification.Domain;

namespace Songly.Notification.Inbox;

/// <summary>
/// Moderation decisions, turned into notifications to both sides (original ADR 0012): the one a decision
/// restricts or answers, and the moderators a report or an appeal calls to work. These types get through
/// a block and are shown without the moderator's name (<see cref="NotificationTypes"/>).
/// </summary>
/// <remarks>
/// The gates "only a new ban" and "only the first pending report" sit at the publishers, so every
/// channel of this module inherits them.
/// </remarks>
internal sealed class RecordModerationNotifications(NotificationService inbox)
    : IDomainEventHandler<CommunityMemberBanned>,
      IDomainEventHandler<CommunityBanAppealSubmitted>,
      IDomainEventHandler<CommunityBanAppealAccepted>,
      IDomainEventHandler<CommunityBanAppealRejected>,
      IDomainEventHandler<CommunityBanEscalationAccepted>,
      IDomainEventHandler<CommunityBanEscalationRejected>,
      IDomainEventHandler<CommunityPostRemoved>,
      IDomainEventHandler<CommunityCommentRemoved>,
      IDomainEventHandler<PostReported>,
      IDomainEventHandler<PostReportDismissed>
{
    /// <summary>Without it the banned user learnt of the ban only when trying to write — a moderation decision looking like a failure of the app.</summary>
    public Task HandleAsync(CommunityMemberBanned domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.UserId, domainEvent.ModeratorId, NotificationType.CommunityBanned, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    /// <summary>To every moderator, like a report: a queue without a notification is a tool nothing starts (original ADR 0017).</summary>
    public Task HandleAsync(CommunityBanAppealSubmitted domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordForModeratorsAsync(domainEvent.CommunityId, domainEvent.UserId, NotificationType.BanAppealSubmitted, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityBanAppealAccepted domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.UserId, domainEvent.ModeratorId, NotificationType.BanAppealAccepted, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityBanAppealRejected domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.UserId, domainEvent.ModeratorId, NotificationType.BanAppealRejected, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    /// <summary>The second instance (original ADR 0028) — an answer to one's own petition, so it cannot be declined either.</summary>
    public Task HandleAsync(CommunityBanEscalationAccepted domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.UserId, domainEvent.AdminId, NotificationType.BanEscalationAccepted, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityBanEscalationRejected domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.UserId, domainEvent.AdminId, NotificationType.BanEscalationRejected, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    /// <summary>The resource is the community: the post is gone.</summary>
    public Task HandleAsync(CommunityPostRemoved domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.PostAuthorId, domainEvent.ModeratorId, NotificationType.CommunityPostRemoved, domainEvent.CommunityId, domainEvent.OccurredAt, cancellationToken);

    /// <summary>
    /// Without it a comment would vanish without a word — to its author it would look like a bug, and one
    /// cannot learn from a decision one does not know of (original ADR 0012). The resource is the post.
    /// </summary>
    public Task HandleAsync(CommunityCommentRemoved domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.CommentAuthorId, domainEvent.ModeratorId, NotificationType.CommunityCommentRemoved, domainEvent.PostId, domainEvent.OccurredAt, cancellationToken);

    /// <summary>
    /// To all moderators at once — the half of moderation the queue does not do by itself. A queue needs
    /// someone to look into it; a notification says there is a reason to.
    /// </summary>
    public Task HandleAsync(PostReported domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordForModeratorsAsync(domainEvent.CommunityId, domainEvent.ReporterId, NotificationType.CommunityPostReported, domainEvent.PostId, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(PostReportDismissed domainEvent, CancellationToken cancellationToken) =>
        inbox.RecordAsync(domainEvent.ReporterId, domainEvent.ModeratorId, NotificationType.PostReportDismissed, domainEvent.PostId, domainEvent.OccurredAt, cancellationToken);
}
