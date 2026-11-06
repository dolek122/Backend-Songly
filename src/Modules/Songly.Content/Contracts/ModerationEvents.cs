namespace Songly.Content.Contracts;

/// <summary>
/// A moderator removed someone else's post in a community — <b>only</b> when someone other than the author
/// deletes it: deleting one's own post is no moderation decision.
/// </summary>
/// <remarks>
/// It carries the community, not the post: the post stops existing at this moment, so an id for the
/// client to open would lead nowhere. <paramref name="ReporterIds"/> are the people who reported the post
/// <b>before</b> the removal — the cascade takes the reports along, and the removal is exactly the moment a
/// report turns out to be right.
/// </remarks>
public sealed record CommunityPostRemoved(Guid CommunityId, Guid PostAuthorId, Guid ModeratorId, IReadOnlySet<Guid> ReporterIds, DateTimeOffset OccurredAt);

/// <summary>
/// A moderator removed someone else's comment under a post in a community — only when someone other than
/// the author deletes it, for the same reason as <see cref="CommunityPostRemoved"/>. It carries the post,
/// which, unlike a removed post, still exists and is where the notification can lead.
/// </summary>
public sealed record CommunityCommentRemoved(Guid CommunityId, Guid PostId, Guid CommentAuthorId, Guid ModeratorId, DateTimeOffset OccurredAt);

/// <summary>
/// A post entered the moderation queue: reported while it had no pending report (original ADR 0049 —
/// earlier, dismissed reports counted too, so a post reported again after its reports were dismissed got
/// into the queue without a word).
/// </summary>
/// <remarks>
/// Every moderator of the community is told, so without the "first" a post reported by twenty people
/// would bury three moderators under sixty notifications of one case. The further reports raise the count
/// in the queue, and that count says how many independent people object.
/// </remarks>
public sealed record PostReported(Guid CommunityId, Guid PostId, Guid ReporterId, DateTimeOffset OccurredAt);

/// <summary>
/// A report dismissed — the moderator decided the post stays. The reporter is told: otherwise reporting is
/// dropping notes into a locked box, and after a few times one stops reporting anything.
/// </summary>
public sealed record PostReportDismissed(Guid CommunityId, Guid PostId, Guid ReporterId, Guid ModeratorId, DateTimeOffset OccurredAt);
