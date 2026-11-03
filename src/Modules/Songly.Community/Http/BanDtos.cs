using Songly.Common.Validation;

namespace Songly.Community.Http;

internal enum BanAppealStatusDto
{
    Pending,
    Rejected,
    Escalated,
    EscalationRejected,
}

/// <summary>
/// The reason is optional but kept: bans are lifted months later, by someone else than who imposed
/// them. <b>The banned user sees it too</b> (original ADR 0015) — it is not a private note of the
/// moderators. <c>durationDays</c>: <c>null</c> is permanent; a day is the smallest unit on purpose,
/// and a hundred years the ceiling — a longer term only pushed <c>expires_at</c> out of Postgres's
/// timestamp range and ended in a 500 (original ADR 0049).
/// </summary>
internal sealed record BanMemberRequest
{
    [Size(0, 500)]
    public string? Reason { get; init; }

    [Min(1)]
    [Max(36_500)]
    public int? DurationDays { get; init; }
}

/// <summary>The text is required — an appeal without a reason gives the moderators nothing to weigh.</summary>
internal sealed record BanAppealRequest
{
    [NotBlank]
    [Size(0, 1000)]
    public string? Text { get; init; }
}

/// <summary>
/// The caller's own ban, seen by them (original ADR 0015): reason and term, without the moderator's
/// identity — the ban belongs to the community; the reason serves understanding, pointing at a
/// person would serve retaliation. The appeal's state, not its text: they wrote it.
/// </summary>
internal sealed record BanDetailsResponse(string? Reason, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, BanAppealStatusDto? AppealStatus, DateTimeOffset? AppealedAt);

/// <summary>
/// The moderators' list of active bans — also the queue of appeals, and the only place their text
/// is read: e-mails and notifications only say there is something to look at.
/// </summary>
internal sealed record CommunityBanResponse(
    Guid UserId,
    string? DisplayName,
    string? AvatarUrl,
    string? Reason,
    Guid? BannedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? AppealText,
    DateTimeOffset? AppealedAt,
    BanAppealStatusDto? AppealStatus);

/// <summary>
/// A case waiting for the second instance (original ADR 0028), with both sides — the ban's reason and
/// the appeal — because the administrator decides between them. The only place someone outside the
/// community reads its ban reasons, and only for cases the banned user sent there.
/// </summary>
internal sealed record EscalatedAppealResponse(
    Guid CommunityId,
    string? CommunityName,
    Guid UserId,
    string? Reason,
    DateTimeOffset BannedAt,
    DateTimeOffset? ExpiresAt,
    string? AppealText,
    DateTimeOffset? AppealedAt,
    DateTimeOffset? EscalatedAt);
