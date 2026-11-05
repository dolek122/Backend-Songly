namespace Songly.Notification.Http;

/// <param name="ActorDisplayName">Absent for moderation decisions — the decision is the community's, not a person's (original ADR 0045).</param>
/// <param name="ReadAt"><c>null</c> while unread.</param>
internal sealed record NotificationResponse(
    Guid Id,
    NotificationTypeDto Type,
    Guid ActorId,
    string? ActorDisplayName,
    string? ActorAvatarUrl,
    Guid ResourceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

internal sealed record UnreadCountResponse(long Count);

/// <summary>The notification types as the API names them — the domain's own enum stays inside.</summary>
internal enum NotificationTypeDto
{
    PostLiked,
    PostCommented,
    PostShared,
    FriendRequestReceived,
    FriendRequestAccepted,
    CommunityMemberJoined,
    CommunityBanned,
    CommunityPostRemoved,
    CommunityCommentRemoved,
    CommunityPostReported,
    BanEscalationAccepted,
    BanEscalationRejected,
    PostReportDismissed,
    BanAppealSubmitted,
    BanAppealAccepted,
    BanAppealRejected,
    CommunityModeratorGranted,
    CommunityModeratorRevoked,
}
