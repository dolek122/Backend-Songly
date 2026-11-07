using Microsoft.EntityFrameworkCore;
using Songly.Common.Paging;
using Songly.Community.Contracts;
using Songly.ModerationLog.Domain;
using Songly.ModerationLog.Http;
using Songly.ModerationLog.Persistence;

namespace Songly.ModerationLog.History;

/// <summary>
/// Two readings of the history, for two different audiences (original ADR 0024). Paged, unlike the lists of
/// state (admins, suspensions): those are small by nature, the log grows without bound.
/// </summary>
internal sealed class ModerationLogService(ModerationLogDbContext db, ICommunityMembership communities)
{
    /// <summary>One community's history — for its moderators; only the community module knows who they are.</summary>
    public async Task<PagedResponse<ModerationLogEntryResponse>> CommunityLogAsync(Guid communityId, Guid requesterId, PageQuery page, CancellationToken cancellationToken)
    {
        if (!await communities.ExistsAsync(communityId, cancellationToken))
        {
            throw new CommunityLogNotFoundException();
        }

        if (!await communities.CanModerateAsync(communityId, requesterId, cancellationToken))
        {
            throw new NotCommunityModeratorException();
        }

        return await NewestFirst(db.Entries.Where(e => e.CommunityId == communityId)).ToPageAsync(page, ToResponse, cancellationToken);
    }

    /// <summary>
    /// The whole platform's history — for the administrators (the role is checked by the path). The optional
    /// filter answers a question nothing could answer before: "what does this person have behind them".
    /// </summary>
    public Task<PagedResponse<ModerationLogEntryResponse>> PlatformLogAsync(Guid? subjectId, PageQuery page, CancellationToken cancellationToken)
    {
        var entries = subjectId is { } id ? db.Entries.Where(e => e.SubjectId == id) : db.Entries;
        return NewestFirst(entries).ToPageAsync(page, ToResponse, cancellationToken);
    }

    // Entries of one decision share the instant (a removed post and its upheld reports), so the id keeps the
    // order of a page stable.
    private static IQueryable<ModerationLogEntry> NewestFirst(IQueryable<ModerationLogEntry> entries) =>
        entries.AsNoTracking().OrderByDescending(e => e.OccurredAt).ThenBy(e => e.Id);

    private static ModerationLogEntryResponse ToResponse(ModerationLogEntry entry) =>
        new(ToDto(entry.Action), entry.CommunityId, entry.SubjectId, entry.ActorId, entry.ExpiresAt, entry.ReportCount, entry.OccurredAt);

    private static ModerationActionDto ToDto(ModerationAction action) => action switch
    {
        ModerationAction.CommunityBanIssued => ModerationActionDto.CommunityBanIssued,
        ModerationAction.CommunityBanLifted => ModerationActionDto.CommunityBanLifted,
        ModerationAction.CommunityPostRemoved => ModerationActionDto.CommunityPostRemoved,
        ModerationAction.CommunityCommentRemoved => ModerationActionDto.CommunityCommentRemoved,
        ModerationAction.PostReportDismissed => ModerationActionDto.PostReportDismissed,
        ModerationAction.PostReportUpheld => ModerationActionDto.PostReportUpheld,
        ModerationAction.CommunityBanAppealSubmitted => ModerationActionDto.CommunityBanAppealSubmitted,
        ModerationAction.CommunityBanAppealAccepted => ModerationActionDto.CommunityBanAppealAccepted,
        ModerationAction.CommunityBanAppealRejected => ModerationActionDto.CommunityBanAppealRejected,
        ModerationAction.CommunityBanAppealEscalated => ModerationActionDto.CommunityBanAppealEscalated,
        ModerationAction.CommunityBanEscalationAccepted => ModerationActionDto.CommunityBanEscalationAccepted,
        ModerationAction.CommunityBanEscalationRejected => ModerationActionDto.CommunityBanEscalationRejected,
        ModerationAction.CommunityModeratorGranted => ModerationActionDto.CommunityModeratorGranted,
        ModerationAction.CommunityModeratorRevoked => ModerationActionDto.CommunityModeratorRevoked,
        ModerationAction.AccountSuspended => ModerationActionDto.AccountSuspended,
        ModerationAction.AccountSuspensionLifted => ModerationActionDto.AccountSuspensionLifted,
        ModerationAction.SuspensionAppealSubmitted => ModerationActionDto.SuspensionAppealSubmitted,
        ModerationAction.SuspensionAppealAccepted => ModerationActionDto.SuspensionAppealAccepted,
        ModerationAction.SuspensionAppealRejected => ModerationActionDto.SuspensionAppealRejected,
        ModerationAction.PlatformAdminGranted => ModerationActionDto.PlatformAdminGranted,
        ModerationAction.PlatformAdminRevoked => ModerationActionDto.PlatformAdminRevoked,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
