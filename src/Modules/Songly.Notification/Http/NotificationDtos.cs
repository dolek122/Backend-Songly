using Songly.Common.Validation;

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

/// <summary>The e-mail preferences (original ADR 0020). The inbox is not here on purpose — it is always complete and no setting governs it.</summary>
internal sealed record MailPreferencesResponse(bool DecisionsAboutMe, bool ModerationQueue);

/// <summary>
/// Both fields required: <c>PUT</c> replaces the settings whole, so a missing field would mean "leave it" and
/// quietly turn this into a partial update.
/// </summary>
internal sealed record UpdateMailPreferencesRequest
{
    [NotNull]
    public bool? DecisionsAboutMe { get; init; }

    [NotNull]
    public bool? ModerationQueue { get; init; }
}

internal enum PushPlatformDto
{
    Android,
    Ios,
}

/// <summary>A device's FCM token (original ADR 0040). The limit has room — today's tokens are about 160 characters.</summary>
internal sealed record RegisterPushDeviceRequest
{
    [NotBlank]
    [Size(0, 512)]
    public string? Token { get; init; }

    [NotNull]
    public PushPlatformDto? Platform { get; init; }
}

/// <summary>
/// The token in the body, not the path: FCM tokens contain <c>:</c> and are long, and a path with a device's
/// id would land in access logs.
/// </summary>
internal sealed record RemovePushDeviceRequest
{
    [NotBlank]
    [Size(0, 512)]
    public string? Token { get; init; }
}
