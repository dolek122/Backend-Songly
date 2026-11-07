namespace Songly.Analytics.Http;

/// <param name="Since">The start of the window: now minus <paramref name="WindowDays"/>.</param>
/// <param name="Counts">Every type, a zero included — a missing type would read as "not counted", not "did not happen".</param>
internal sealed record AnalyticsSummaryResponse(int WindowDays, DateTimeOffset Since, IReadOnlyList<AnalyticsCountResponse> Counts);

internal sealed record AnalyticsCountResponse(AnalyticsEventTypeDto Type, long Count);

/// <summary>The counted types as the API names them — the domain's own enum stays inside.</summary>
internal enum AnalyticsEventTypeDto
{
    UserRegistered,
    PostLiked,
    PostCommented,
    PostShared,
    FriendRequestSent,
    FriendRequestAccepted,
    CommunityMemberJoined,
    PostFirstReported,
    PostReportDismissed,
    CommunityPostRemoved,
    CommunityCommentRemoved,
    CommunityBanIssuedPermanent,
    CommunityBanIssuedTemporary,
    BanAppealSubmitted,
    BanAppealAccepted,
    BanAppealRejected,
}
