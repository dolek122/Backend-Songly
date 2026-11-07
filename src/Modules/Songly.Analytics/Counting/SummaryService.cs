using Microsoft.EntityFrameworkCore;
using Songly.Analytics.Domain;
using Songly.Analytics.Http;
using Songly.Analytics.Persistence;

namespace Songly.Analytics.Counting;

/// <summary>The counts of the last days, for the administrators (original ADR 0004).</summary>
internal sealed class SummaryService(AnalyticsDbContext db, TimeProvider time)
{
    public const int MaxWindowDays = 365;

    /// <summary>The window is clamped, not rejected — 0 or less is one day, more than a year is a year.</summary>
    public async Task<AnalyticsSummaryResponse> SummaryAsync(int windowDays, CancellationToken cancellationToken)
    {
        var days = Math.Clamp(windowDays, 1, MaxWindowDays);
        var since = time.GetUtcNow().AddDays(-days);
        var counts = await db.Events.AsNoTracking()
            .Where(e => e.OccurredAt >= since)
            .GroupBy(e => e.Type)
            .Select(group => new { Type = group.Key, Count = group.LongCount() })
            .ToDictionaryAsync(row => row.Type, row => row.Count, cancellationToken);
        return new AnalyticsSummaryResponse(
            days,
            since,
            [.. Enum.GetValues<AnalyticsEventType>().Select(type => new AnalyticsCountResponse(ToDto(type), counts.GetValueOrDefault(type)))]);
    }

    private static AnalyticsEventTypeDto ToDto(AnalyticsEventType type) => type switch
    {
        AnalyticsEventType.UserRegistered => AnalyticsEventTypeDto.UserRegistered,
        AnalyticsEventType.PostLiked => AnalyticsEventTypeDto.PostLiked,
        AnalyticsEventType.PostCommented => AnalyticsEventTypeDto.PostCommented,
        AnalyticsEventType.PostShared => AnalyticsEventTypeDto.PostShared,
        AnalyticsEventType.FriendRequestSent => AnalyticsEventTypeDto.FriendRequestSent,
        AnalyticsEventType.FriendRequestAccepted => AnalyticsEventTypeDto.FriendRequestAccepted,
        AnalyticsEventType.CommunityMemberJoined => AnalyticsEventTypeDto.CommunityMemberJoined,
        AnalyticsEventType.PostFirstReported => AnalyticsEventTypeDto.PostFirstReported,
        AnalyticsEventType.PostReportDismissed => AnalyticsEventTypeDto.PostReportDismissed,
        AnalyticsEventType.CommunityPostRemoved => AnalyticsEventTypeDto.CommunityPostRemoved,
        AnalyticsEventType.CommunityCommentRemoved => AnalyticsEventTypeDto.CommunityCommentRemoved,
        AnalyticsEventType.CommunityBanIssuedPermanent => AnalyticsEventTypeDto.CommunityBanIssuedPermanent,
        AnalyticsEventType.CommunityBanIssuedTemporary => AnalyticsEventTypeDto.CommunityBanIssuedTemporary,
        AnalyticsEventType.BanAppealSubmitted => AnalyticsEventTypeDto.BanAppealSubmitted,
        AnalyticsEventType.BanAppealAccepted => AnalyticsEventTypeDto.BanAppealAccepted,
        AnalyticsEventType.BanAppealRejected => AnalyticsEventTypeDto.BanAppealRejected,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
