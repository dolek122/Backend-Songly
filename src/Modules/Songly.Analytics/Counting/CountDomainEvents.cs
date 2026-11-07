using Songly.Analytics.Domain;
using Songly.Common.Events;
using Songly.Common.Persistence;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Identity.Contracts;
using Songly.Social.Contracts;

namespace Songly.Analytics.Counting;

/// <summary>
/// The module's only contact with the rest of the system: the same events the notification module reacts to
/// (original Claude.md §2) — no reaching for other modules' entities or services, and no new events for it.
/// </summary>
/// <remarks>
/// The moderation decisions (original ADR 0011–0018) left no trace in analytics at first, so the decisions
/// postponed "until the data shows" had nowhere to take the data from (ADR 0019). They are counted from the same
/// events, without touching the modules that publish them.
/// </remarks>
internal sealed class CountDomainEvents(AnalyticsRecorder recorder, IAfterCommit afterCommit)
    : IDomainEventHandler<UserRegistered>,
      IDomainEventHandler<PostLiked>,
      IDomainEventHandler<PostCommented>,
      IDomainEventHandler<PostShared>,
      IDomainEventHandler<FriendRequestSent>,
      IDomainEventHandler<FriendRequestAccepted>,
      IDomainEventHandler<CommunityMemberJoined>,
      IDomainEventHandler<PostReported>,
      IDomainEventHandler<PostReportDismissed>,
      IDomainEventHandler<CommunityPostRemoved>,
      IDomainEventHandler<CommunityCommentRemoved>,
      IDomainEventHandler<CommunityMemberBanned>,
      IDomainEventHandler<CommunityBanAppealSubmitted>,
      IDomainEventHandler<CommunityBanAppealAccepted>,
      IDomainEventHandler<CommunityBanAppealRejected>
{
    /// <summary>
    /// <see cref="UserRegistered"/> is published inside the account's transaction (ADR 0008), so it is counted
    /// only after that commits — a registration rolled back is no registration, and a failed count must not take
    /// the account down with it.
    /// </summary>
    public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken)
    {
        afterCommit.Defer(token => recorder.RecordAsync(AnalyticsEventType.UserRegistered, domainEvent.OccurredAt, token));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PostLiked domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.PostLiked, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(PostCommented domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.PostCommented, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(PostShared domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.PostShared, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(FriendRequestSent domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.FriendRequestSent, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(FriendRequestAccepted domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.FriendRequestAccepted, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityMemberJoined domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.CommunityMemberJoined, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(PostReported domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.PostFirstReported, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(PostReportDismissed domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.PostReportDismissed, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityPostRemoved domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.CommunityPostRemoved, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityCommentRemoved domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.CommunityCommentRemoved, domainEvent.OccurredAt, cancellationToken);

    /// <summary>Split by the term — the counters carry types only, so otherwise nobody could tell whether temporary bans caught on.</summary>
    public Task HandleAsync(CommunityMemberBanned domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(
            domainEvent.ExpiresAt is null ? AnalyticsEventType.CommunityBanIssuedPermanent : AnalyticsEventType.CommunityBanIssuedTemporary,
            domainEvent.OccurredAt,
            cancellationToken);

    public Task HandleAsync(CommunityBanAppealSubmitted domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.BanAppealSubmitted, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityBanAppealAccepted domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.BanAppealAccepted, domainEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(CommunityBanAppealRejected domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(AnalyticsEventType.BanAppealRejected, domainEvent.OccurredAt, cancellationToken);
}
