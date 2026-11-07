namespace Songly.Analytics.Domain;

internal enum AnalyticsEventType
{
    UserRegistered,
    PostLiked,
    PostCommented,
    PostShared,
    FriendRequestSent,
    FriendRequestAccepted,
    CommunityMemberJoined,

    /// <summary>
    /// <b>Posts reported for the first time</b>, not the number of reports — on purpose (original ADR 0019). The
    /// event is published only when a post enters the queue, the anti-spam gate sits at the publisher, and
    /// moving it to the consumers would duplicate it. So this answers "how much content draws objections", not
    /// "how many times was report clicked"; the latter is in <c>post_reports</c>, which has its dates.
    /// </summary>
    PostFirstReported,

    /// <summary>Reports dismissed by the moderation — the post stays.</summary>
    PostReportDismissed,

    /// <summary>Posts removed by a moderator; one's own deletions are not here.</summary>
    CommunityPostRemoved,

    /// <summary>Comments removed by a moderator; one's own deletions are not here.</summary>
    CommunityCommentRemoved,

    /// <summary>
    /// Split into permanent and temporary, because that is the only way to see whether temporary bans (original
    /// ADR 0014) caught on at all — the counters have types, no dimensions (ADR 0004). Both together are all new
    /// bans.
    /// </summary>
    CommunityBanIssuedPermanent,
    CommunityBanIssuedTemporary,

    /// <summary>
    /// Appeals (original ADR 0017). Next to the bans they say whether the channel is used at all; accepted to
    /// submitted says how often the moderation decides itself that it was wrong — a measure there was nowhere to
    /// take from before.
    /// </summary>
    BanAppealSubmitted,
    BanAppealAccepted,
    BanAppealRejected,
}

/// <summary>
/// One counted occurrence (<c>analytics_events</c>). Deliberately without a user or a resource — counts of "what
/// happens", not behavioural data per person (original Claude.md §9, ADR 0004).
/// </summary>
internal sealed class AnalyticsEvent
{
    // For EF Core.
    private AnalyticsEvent()
    {
    }

    public AnalyticsEvent(AnalyticsEventType type, DateTimeOffset occurredAt)
    {
        Id = Guid.NewGuid();
        Type = type;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public AnalyticsEventType Type { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
