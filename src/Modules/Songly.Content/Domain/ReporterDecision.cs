namespace Songly.Content.Domain;

/// <summary>
/// How a report ended (original ADR 0030). Not <see cref="ReportStatus"/>: that one also has
/// "pending", and a decision row exists only once the decision is made — a type that allows
/// "undecided" would invite writing such a row.
/// </summary>
internal enum ReporterDecisionOutcome
{
    /// <summary>The post was deleted by moderation — the report was right.</summary>
    Upheld,

    /// <summary>The moderator decided the post stays.</summary>
    Dismissed,
}

/// <summary>
/// One decision on a report, written when it is made (original ADR 0027), <b>with its date</b>
/// (ADR 0030).
/// </summary>
/// <remarks>
/// <para>
/// Not tied to the post, and that is why it exists: upholding a report deletes the post, which
/// deletes its reports, so counting over <c>post_reports</c> lost the history exactly when a
/// report turned out to be <b>right</b>.
/// </para>
/// <para>
/// A row per decision rather than a counter: a counter does not know when what it counted came,
/// so "dismissals of the last 90 days" cannot be read from it. A date on a counter would give a
/// <b>stepped</b> window — zeroing the whole row at the boundary would drop yesterday's dismissal
/// together with last year's, and a patient troll would get a clean slate by aiming at the moment.
/// </para>
/// </remarks>
internal sealed class ReporterDecision(Guid userId, Guid communityId, ReporterDecisionOutcome outcome, DateTimeOffset decidedAt)
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid UserId { get; private set; } = userId;

    public Guid CommunityId { get; private set; } = communityId;

    public ReporterDecisionOutcome Outcome { get; private set; } = outcome;

    public DateTimeOffset DecidedAt { get; private set; } = decidedAt;
}
