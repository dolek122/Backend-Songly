using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Songly.Common.Paging;
using Songly.Community.Contracts;
using Songly.Content.Domain;
using Songly.Content.Http;
using Songly.Content.Persistence;
using Songly.Content.Posts;
using Songly.Profile.Contracts;

namespace Songly.Content.Reports;

/// <summary>Reporting posts to a community's moderation, and the moderators' queue.</summary>
/// <remarks>
/// The original publishes <c>PostReportedEvent</c> when a post enters the queue (it had no pending
/// report before — only then are all moderators called, original ADR 0012, 0049) and
/// <c>PostReportDismissedEvent</c>; they arrive with the notification, analytics and moderation-log
/// modules, together with the mail interval for moderators (ADR 0018), which belongs to the mail.
/// </remarks>
internal sealed class ReportService(
    ContentDbContext db,
    ContentVisibility visibility,
    PostViews views,
    ReporterHistory history,
    ICommunityMembership communities,
    IProfileDirectory profiles,
    ModerationMetrics metrics,
    IOptions<ReportOptions> options,
    TimeProvider time)
{
    /// <summary>
    /// Reports a post. Open to anyone signed in — non-members too: communities are open for reading,
    /// so their content is seen more widely than by their members.
    /// </summary>
    /// <remarks>
    /// The channel has three gates and none replaces the others (original ADR 0018):
    /// <list type="number">
    /// <item><b>A ban blocks reporting.</b> A ban cuts off the paths that reach people and leaves the
    /// pure numbers; a report carries an identity and text straight to the moderators.</item>
    /// <item><b>One report per post and reporter</b> — otherwise one person would push the count up
    /// for ever, and the queue would show persistence instead of how many independent people object.</item>
    /// <item><b>A limit per person</b> across posts, the one the per-post gate cannot see — lowered by
    /// the reporter's history (<see cref="ReporterHistory.AllowanceAsync"/>). A dismissed report still
    /// takes its place in the window, so the lowered allowance shows only in the next window.</item>
    /// </list>
    /// A post the reporter may not see (a block) does not exist for them: reporting must not be a way
    /// around that.
    /// </remarks>
    public async Task ReportAsync(Guid postId, Guid reporterId, ReportPostRequest request, CancellationToken cancellationToken)
    {
        var post = await visibility.VisiblePostAsync(postId, reporterId, cancellationToken);
        if (post.CommunityId is not { } communityId)
        {
            throw new PostNotInCommunityException();
        }

        if (post.AuthorId == reporterId)
        {
            throw new CannotReportOwnPostException();
        }

        if (await communities.IsBannedAsync(communityId, reporterId, cancellationToken))
        {
            metrics.ReportRejected("banned");
            throw new BannedFromCommunityException();
        }

        if (await db.PostReports.AnyAsync(r => r.PostId == postId && r.ReporterId == reporterId, cancellationToken))
        {
            throw new PostAlreadyReportedException();
        }

        var now = time.GetUtcNow();
        var windowStart = now - options.Value.Window;
        var recent = await db.PostReports.CountAsync(r => r.ReporterId == reporterId && r.CreatedAt > windowStart, cancellationToken);
        if (recent >= await history.AllowanceAsync(reporterId, cancellationToken))
        {
            metrics.ReportRejected("rate-limit");
            throw new ReportRateLimitExceededException();
        }

        db.PostReports.Add(new PostReport(postId, reporterId, ReportMapping.ToDomain(request.Reason!.Value), request.Details, now));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The pending reports of one community, oldest first — a queue, not a notice board. The posts are
    /// shown to the moderator without the block filter: a moderator who blocked a troll must still see
    /// what was reported, or the queue would quietly die while looking alive.
    /// </summary>
    public async Task<PagedResponse<PostReportResponse>> PendingAsync(Guid communityId, Guid requesterId, PageQuery page, CancellationToken cancellationToken)
    {
        await RequireModeratorAsync(communityId, requesterId, cancellationToken);

        // The post, not the report, knows its community — a copy of community_id on the report would
        // be a second source of truth.
        var reports = await db.PostReports.AsNoTracking()
            .Where(r => r.Status == ReportStatus.Pending && db.Posts.Any(p => p.Id == r.PostId && p.CommunityId == communityId))
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToPageAsync(page, report => report, cancellationToken);
        if (reports.Content.Count == 0)
        {
            return new PagedResponse<PostReportResponse>([], reports.Page);
        }

        var postIds = reports.Content.Select(r => r.PostId).Distinct().ToList();
        var posts = (await views.ViewsAsync(await db.Posts.AsNoTracking().Where(p => postIds.Contains(p.Id)).ToListAsync(cancellationToken), requesterId, [], cancellationToken))
            .ToDictionary(p => p.Id);
        var reportCounts = await db.PostReports
            .Where(r => postIds.Contains(r.PostId) && r.Status == ReportStatus.Pending)
            .GroupBy(r => r.PostId).Select(g => new { g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var reporterIds = reports.Content.Select(r => r.ReporterId).Distinct().ToList();
        var names = await profiles.DisplayNamesAsync(reporterIds, cancellationToken);
        var track = await history.InCommunityAsync(communityId, reporterIds, cancellationToken);

        return new PagedResponse<PostReportResponse>(
            reports.Content.Select(r => new PostReportResponse(
                r.Id,
                ReportMapping.ToDto(r.Reason),
                r.Details,
                r.ReporterId,
                names.GetValueOrDefault(r.ReporterId),
                r.CreatedAt,
                reportCounts.GetValueOrDefault(r.PostId),
                track[r.ReporterId].Upheld,
                track[r.ReporterId].Dismissed,
                posts[r.PostId])).ToList(),
            reports.Page);
    }

    /// <summary>
    /// Dismisses a report: the moderator decided the post stays. There is no "uphold" on purpose —
    /// upholding <b>is</b> deleting the post (<c>DELETE /posts/{id}</c>), which takes its reports along;
    /// a separate button would be a second path to the same result.
    /// </summary>
    /// <remarks>
    /// Idempotent, like the other moderation decisions: a second click or a second moderator records
    /// no second decision. Before, every repetition added a dismissal — another penalty on the
    /// reporter's allowance — so one report was enough to push someone's allowance to the floor. The
    /// conditional update makes it hold for two moderators at the same moment too: only the statement
    /// that changed the row writes the decision.
    /// </remarks>
    public async Task DismissAsync(Guid communityId, Guid reportId, Guid requesterId, CancellationToken cancellationToken)
    {
        await RequireModeratorAsync(communityId, requesterId, cancellationToken);
        var report = await db.PostReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reportId, cancellationToken) ?? throw new ReportNotFoundException();

        // A report from another community is reported as nonexistent, not as forbidden.
        if (!await db.Posts.AnyAsync(p => p.Id == report.PostId && p.CommunityId == communityId, cancellationToken))
        {
            throw new ReportNotFoundException();
        }

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var dismissed = await db.PostReports
            .Where(r => r.Id == reportId && r.Status == ReportStatus.Pending)
            .ExecuteUpdateAsync(
                r => r.SetProperty(x => x.Status, ReportStatus.Dismissed).SetProperty(x => x.ReviewedBy, requesterId).SetProperty(x => x.ReviewedAt, now),
                cancellationToken);
        if (dismissed == 1)
        {
            history.Record(report.ReporterId, communityId, ReporterDecisionOutcome.Dismissed);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RequireModeratorAsync(Guid communityId, Guid requesterId, CancellationToken cancellationToken)
    {
        if (!await communities.ExistsAsync(communityId, cancellationToken))
        {
            throw new UnknownCommunityException();
        }

        if (!await communities.CanModerateAsync(communityId, requesterId, cancellationToken))
        {
            throw new NotCommunityModeratorException();
        }
    }
}

/// <summary>Domain ↔ HTTP copies of the reason; endpoints never see domain types.</summary>
internal static class ReportMapping
{
    public static ReportReason ToDomain(ReportReasonDto reason) => reason switch
    {
        ReportReasonDto.Spam => ReportReason.Spam,
        ReportReasonDto.Harassment => ReportReason.Harassment,
        ReportReasonDto.HateSpeech => ReportReason.HateSpeech,
        ReportReasonDto.IllegalContent => ReportReason.IllegalContent,
        ReportReasonDto.Other => ReportReason.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static ReportReasonDto ToDto(ReportReason reason) => reason switch
    {
        ReportReason.Spam => ReportReasonDto.Spam,
        ReportReason.Harassment => ReportReasonDto.Harassment,
        ReportReason.HateSpeech => ReportReasonDto.HateSpeech,
        ReportReason.IllegalContent => ReportReasonDto.IllegalContent,
        ReportReason.Other => ReportReasonDto.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
