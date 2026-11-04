using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Songly.Content.Domain;
using Songly.Content.Persistence;

namespace Songly.Content.Reports;

/// <summary>
/// The reporters' track record: how a person's reports ended, and what that does to how many they
/// may file (original ADR 0025, 0027, 0030).
/// </summary>
/// <remarks>
/// Counted over this module's own rows, not over the moderation log: the log lives in a module that
/// consumes content's events, so reading it from here would be a cycle.
/// </remarks>
internal sealed class ReporterHistory(ContentDbContext db, IOptions<ReportOptions> options, TimeProvider time)
{
    /// <summary>A new decision row — always a new row, never an update; the caller saves it with the decision itself.</summary>
    public void Record(Guid reporterId, Guid communityId, ReporterDecisionOutcome outcome) =>
        db.ReporterDecisions.Add(new ReporterDecision(reporterId, communityId, outcome, time.GetUtcNow()));

    /// <summary>
    /// The reports one person may file per window: the base limit, lowered by their net dismissals —
    /// <b>globally</b>, across communities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Global, because flooding spread over ten communities slips past any counter kept per
    /// community. It is no leak: the number never leaves the server, it only says "no" to the
    /// reporter themselves.
    /// </para>
    /// <para>
    /// Upheld reports forgive dismissals one for one (ADR 0027): someone with ten upheld and two
    /// dismissed is a good reporter, not a suspect. But they never raise the allowance above the
    /// base — the limit is generous already, and a bonus would open a wider channel for flooding to
    /// someone who once reported well. The floor is never zero (<see cref="ReportOptions.MinLimit"/>).
    /// </para>
    /// </remarks>
    public async Task<int> AllowanceAsync(Guid reporterId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var since = HistoryCutoff();
        var outcomes = await db.ReporterDecisions
            .Where(d => d.UserId == reporterId && d.DecidedAt > since)
            .GroupBy(d => d.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Outcome, g => g.Count, cancellationToken);
        var netDismissed = Math.Max(0, outcomes.GetValueOrDefault(ReporterDecisionOutcome.Dismissed) - outcomes.GetValueOrDefault(ReporterDecisionOutcome.Upheld));
        var reduced = (long)settings.Limit - ((long)netDismissed * settings.DismissalPenalty);
        return (int)Math.Max(settings.MinLimit, Math.Min(settings.Limit, reduced));
    }

    /// <summary>
    /// Upheld and dismissed reports of the given people <b>in one community</b>, within the same
    /// history window as the allowance — for the moderation queue, one query for the whole page.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, (int Upheld, int Dismissed)>> InCommunityAsync(Guid communityId, IReadOnlyCollection<Guid> reporterIds, CancellationToken cancellationToken)
    {
        var since = HistoryCutoff();
        var counts = await db.ReporterDecisions
            .Where(d => d.CommunityId == communityId && reporterIds.Contains(d.UserId) && d.DecidedAt > since)
            .GroupBy(d => new { d.UserId, d.Outcome })
            .Select(g => new { g.Key.UserId, g.Key.Outcome, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return reporterIds.Distinct().ToDictionary(
            id => id,
            id => (
                counts.Where(c => c.UserId == id && c.Outcome == ReporterDecisionOutcome.Upheld).Sum(c => c.Count),
                counts.Where(c => c.UserId == id && c.Outcome == ReporterDecisionOutcome.Dismissed).Sum(c => c.Count)));
    }

    /// <summary>Decisions older than this count for nothing — rows before it are read by nobody.</summary>
    private DateTimeOffset HistoryCutoff() => time.GetUtcNow() - options.Value.HistoryWindow;
}
