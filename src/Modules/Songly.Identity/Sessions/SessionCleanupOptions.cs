namespace Songly.Identity.Sessions;

internal sealed class SessionCleanupOptions
{
    public const string Section = "Songly:Security:SessionCleanup";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a session is kept after it <b>expired</b> before it is deleted. Zero would be
    /// correct — an expired session lets no one in — but a week leaves time to investigate "why
    /// was I logged out" before the trace is gone.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Rows per <c>DELETE</c>. The first run on a neglected table would otherwise be one huge
    /// statement holding its row locks for as long as it runs.
    /// </summary>
    public int BatchSize { get; set; } = 5_000;

    /// <summary>
    /// A ceiling on one run: a neglected table drains over several runs, not in one that holds a
    /// connection for a quarter of an hour.
    /// </summary>
    public int MaxBatchesPerRun { get; set; } = 20;
}
