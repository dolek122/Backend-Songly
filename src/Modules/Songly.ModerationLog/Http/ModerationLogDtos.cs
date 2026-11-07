namespace Songly.ModerationLog.Http;

/// <summary>
/// One entry of the history. <c>actorId</c> is <see langword="null"/> once the decider's account is deleted,
/// <c>communityId</c> for platform decisions; <c>expiresAt</c> and <c>reportCount</c> mean something only for
/// some actions. Without names: the module does not depend on profiles, and the client has them anyway.
/// </summary>
internal sealed record ModerationLogEntryResponse(
    ModerationActionDto Action,
    Guid? CommunityId,
    Guid SubjectId,
    Guid? ActorId,
    DateTimeOffset? ExpiresAt,
    int? ReportCount,
    DateTimeOffset OccurredAt);

/// <summary>The actions as the API names them — the domain's own enum stays inside.</summary>
internal enum ModerationActionDto
{
    CommunityBanIssued,
    CommunityBanLifted,
    CommunityPostRemoved,
    CommunityCommentRemoved,
    PostReportDismissed,
    PostReportUpheld,
    CommunityBanAppealSubmitted,
    CommunityBanAppealAccepted,
    CommunityBanAppealRejected,
    CommunityBanAppealEscalated,
    CommunityBanEscalationAccepted,
    CommunityBanEscalationRejected,
    CommunityModeratorGranted,
    CommunityModeratorRevoked,
    AccountSuspended,
    AccountSuspensionLifted,
    SuspensionAppealSubmitted,
    SuspensionAppealAccepted,
    SuspensionAppealRejected,
    PlatformAdminGranted,
    PlatformAdminRevoked,
}
