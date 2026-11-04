namespace Songly.Content.Reports;

/// <summary>The gates of the report channel (original ADR 0018, 0025, 0027, 0030).</summary>
/// <remarks>
/// Two windows meet in one decision and must not be confused: <see cref="Window"/> measures
/// intensity ("how many reports in an hour"), <see cref="HistoryWindow"/> memory ("how long a
/// mistake lowers the allowance"). Mixing them up would give either a limit without memory or a
/// penalty for an hour.
/// </remarks>
internal sealed class ReportOptions
{
    public const string Section = "Songly:Content:Report";

    /// <summary>
    /// How many reports <b>one person</b> may file per <see cref="Window"/>. The gates "one report
    /// per post and reporter" and "notify only on a post's first report" count per post; neither
    /// limits one person across many posts, so one account could fill the queue with a hundred
    /// reports. A high default on purpose: it cuts off flooding, not a conscientious person
    /// reporting a series of spam during a raid.
    /// </summary>
    public int Limit { get; set; } = 20;

    /// <summary>A sliding window over the reports' own <c>created_at</c> — a gate of the domain, not of the infrastructure.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How much of the allowance one <b>dismissed</b> report costs (original ADR 0025). With 20 and
    /// 5: no dismissals 20 an hour, one 15, two 10, three 5, then <see cref="MinLimit"/>. Linear, not
    /// exponential: a dismissal is a moderator's judgement, not proof of bad faith.
    /// </summary>
    public int DismissalPenalty { get; set; } = 5;

    /// <summary>
    /// The floor of the allowance — <b>never zero</b>, and that is a condition, not caution: reporting
    /// switched off for good would mean a few dismissals take away the tool one could report the
    /// moderator's own abuse with.
    /// </summary>
    public int MinLimit { get; set; } = 1;

    /// <summary>
    /// How long a decided report counts in the reporter's history (original ADR 0030) — dismissals
    /// (the penalty) and upheld reports (forgiving one for one) alike, so both sides of the balance
    /// age together. Ninety days: long enough that flooding does not pay by waiting it out, short
    /// enough that last year's mistakes do not stay with someone for ever. The penalty lapses
    /// lazily — the query simply stops seeing old rows, nothing wakes up at expiry.
    /// </summary>
    public TimeSpan HistoryWindow { get; set; } = TimeSpan.FromDays(90);
}
