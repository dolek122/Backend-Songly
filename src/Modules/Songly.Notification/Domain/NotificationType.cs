namespace Songly.Notification.Domain;

internal enum NotificationType
{
    PostLiked,
    PostCommented,
    PostShared,
    FriendRequestReceived,
    FriendRequestAccepted,
    CommunityMemberJoined,

    /// <summary>To the banned user; the resource is the community.</summary>
    CommunityBanned,

    /// <summary>To the author of a removed post; the resource is the community, since the post is gone.</summary>
    CommunityPostRemoved,

    /// <summary>To the author of a comment removed by moderation; the resource is the post, which, unlike a removed post, still exists.</summary>
    CommunityCommentRemoved,

    /// <summary>To the moderators, when a post gets its first pending report.</summary>
    CommunityPostReported,

    /// <summary>The second instance (original ADR 0028): the platform's decision on a community ban. The resource is the community.</summary>
    BanEscalationAccepted,
    BanEscalationRejected,

    /// <summary>To the reporter, when a moderator decided the post stays.</summary>
    PostReportDismissed,

    /// <summary>To every moderator, when a banned user appeals (original ADR 0017); the resource is the community. At most once per ban.</summary>
    BanAppealSubmitted,

    /// <summary>To the appellant: appeal accepted, ban lifted. The resource is the community.</summary>
    BanAppealAccepted,

    /// <summary>To the appellant: appeal rejected, the ban stays. The resource is the community.</summary>
    BanAppealRejected,

    /// <summary>To the new moderator; the resource is the community.</summary>
    CommunityModeratorGranted,

    /// <summary>
    /// To the former moderator; the resource is the community. Without it losing the role showed only as
    /// 403s on the report queue — a decision looking like a failure, the very problem ADR 0012 began with.
    /// </summary>
    CommunityModeratorRevoked,
}

/// <summary>The groups of types the inbox treats differently. Sets, not flags on the enum, as in the original.</summary>
internal static class NotificationTypes
{
    /// <summary>
    /// Notifications of <b>moderation decisions</b>, as opposed to other people's activity — they get
    /// through a block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rest of the inbox disappears when the actor is blocked either way (original Claude.md §9), as
    /// it should: a block cuts one off from approaches. A moderation decision is not an approach, and it
    /// cannot be declined: under that filter, blocking the moderator would be enough never to learn of
    /// one's own ban or removed post. The block would work against the one who blocks, hiding exactly
    /// what they need to know.
    /// </para>
    /// <para>
    /// The criterion is not "deemed important" but one of two: a decision <b>restricting</b> the
    /// recipient, which they cannot refuse (a ban, a removed post, an answer to their own petition — a
    /// report or an appeal; ADR 0017: one cannot decline an answer one asked for), or a message that
    /// <b>starts the moderation loop</b> (a report, an appeal to review) — under blocks, a moderator who
    /// blocked a troll would stop seeing the troll's cases and the channel would die quietly. Changes of
    /// the moderator role (ADR 0016) are therefore <b>deliberately absent</b>: granting or revoking
    /// rights restricts nothing in ordinary participation, so whoever blocked the owner may also not see
    /// this from them — at their own cost.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlySet<NotificationType> Moderation = new HashSet<NotificationType>
    {
        NotificationType.CommunityBanned,
        NotificationType.CommunityPostRemoved,
        NotificationType.CommunityCommentRemoved,
        NotificationType.CommunityPostReported,
        NotificationType.PostReportDismissed,
        NotificationType.BanAppealSubmitted,
        NotificationType.BanAppealAccepted,
        NotificationType.BanAppealRejected,
        NotificationType.BanEscalationAccepted,
        NotificationType.BanEscalationRejected,
    };

    /// <summary>
    /// Decisions whose actor is a moderator or an administrator deciding about the recipient. The inbox
    /// does <b>not</b> give out the actor's name or avatar for these (original ADR 0045): the decision
    /// belongs to the community (or the platform), not to the person who clicked — pointing a finger
    /// serves retaliation, not understanding.
    /// </summary>
    /// <remarks>
    /// The original's inbox once gave the name out and left hiding it to the client, so every type the
    /// client did not know yet would have shown the moderator by name. The actor's id stays: the
    /// client's contract has it as required.
    /// </remarks>
    public static readonly IReadOnlySet<NotificationType> DecidedByModeration = new HashSet<NotificationType>
    {
        NotificationType.CommunityBanned,
        NotificationType.CommunityPostRemoved,
        NotificationType.CommunityCommentRemoved,
        NotificationType.PostReportDismissed,
        NotificationType.BanAppealAccepted,
        NotificationType.BanAppealRejected,
        NotificationType.BanEscalationAccepted,
        NotificationType.BanEscalationRejected,
    };

    /// <summary>
    /// Notifications of a <b>state</b> the actor can undo and restore (original ADR 0035): a like, a
    /// share, a membership. One per (recipient, actor, resource), however many undo/redo cycles.
    /// </summary>
    /// <remarks>
    /// Without it a <c>DELETE</c>/<c>PUT</c> loop generated notifications in someone else's inbox — every
    /// restored state is a new event to the content module, because from its point of view it is one. To
    /// the recipient a second like of the same post by the same person says nothing new, and only the
    /// inbox, which remembers what it showed, knows that; so the rule lives here, not at the publisher.
    /// Comments are not here: every comment is new content, not a restored state.
    /// </remarks>
    public static readonly IReadOnlySet<NotificationType> OncePerResource = new HashSet<NotificationType>
    {
        NotificationType.PostLiked,
        NotificationType.PostShared,
        NotificationType.CommunityMemberJoined,
    };

    /// <summary>
    /// Notifications collapsed into one <b>while the recipient has not read it</b> (original ADR 0035):
    /// the next one refreshes the existing one (new resource, new date) instead of adding a second.
    /// </summary>
    /// <remarks>
    /// A friend request cannot go by <see cref="OncePerResource"/>: every send is a new row with a new id,
    /// so "once per resource" would not catch a send/cancel loop. "Once per pair for ever" would be too
    /// strong — a former friend inviting again a year later is a real message. Collapsing until read
    /// gives both: a loop adds at most one notification per look into the inbox, and the notification
    /// always points at the request that is actually waiting.
    /// </remarks>
    public static readonly IReadOnlySet<NotificationType> CollapsedWhileUnread = new HashSet<NotificationType>
    {
        NotificationType.FriendRequestReceived,
    };
}
