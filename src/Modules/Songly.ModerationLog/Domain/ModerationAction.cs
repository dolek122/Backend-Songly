namespace Songly.ModerationLog.Domain;

/// <summary>What was decided. <c>expires_at</c> and <c>report_count</c> mean something only for some of them.</summary>
internal enum ModerationAction
{
    /// <summary>With <c>expires_at</c> for a temporary ban.</summary>
    CommunityBanIssued,

    /// <summary>
    /// A lifted ban — by a moderator, an accepted appeal or an overturn by the platform. Without it an expired
    /// ban and a lifted one looked the same: the only trace was the ban's row, and that one is gone.
    /// </summary>
    CommunityBanLifted,

    /// <summary>The subject is the post's author; <c>report_count</c> is how many reports the post had then.</summary>
    CommunityPostRemoved,

    /// <summary>The subject is the comment's author.</summary>
    CommunityCommentRemoved,

    /// <summary>The subject is the <b>reporter</b> — it is their report that was decided.</summary>
    PostReportDismissed,

    /// <summary>
    /// The mirror of <see cref="PostReportDismissed"/>: one per reporter of a removed post (original ADR 0027).
    /// Without it the log knew how many reports a post had, not whose — so not whose reports tend to be right.
    /// </summary>
    PostReportUpheld,

    /// <summary>Subject and actor are the same person: the one decision here the punished person makes.</summary>
    CommunityBanAppealSubmitted,
    CommunityBanAppealAccepted,
    CommunityBanAppealRejected,

    /// <summary>The second instance (original ADR 0028); the actor is the banned person, as with an appeal.</summary>
    CommunityBanAppealEscalated,

    /// <summary>The actor is the administrator — the log shows the decision came from outside the community.</summary>
    CommunityBanEscalationAccepted,
    CommunityBanEscalationRejected,

    CommunityModeratorGranted,
    CommunityModeratorRevoked,

    /// <summary>Platform decisions — without a community, they belong to none. With <c>expires_at</c> when temporary.</summary>
    AccountSuspended,

    /// <summary>
    /// Not after an accepted appeal: identity publishes the acceptance <i>instead</i> of the lifting (one message
    /// to the appellant), so there <see cref="SuspensionAppealAccepted"/> is the entry that ends the suspension.
    /// </summary>
    AccountSuspensionLifted,
    SuspensionAppealSubmitted,
    SuspensionAppealAccepted,
    SuspensionAppealRejected,

    /// <summary>
    /// The only entries that do not restrict their subject but give them power over every other entry here
    /// (original ADR 0031) — "who gave whom that power, and when" belongs to the same timeline.
    /// </summary>
    PlatformAdminGranted,
    PlatformAdminRevoked,
}
