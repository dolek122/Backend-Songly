namespace Songly.Matching;

internal sealed class MatchingOptions
{
    public const string Section = "Songly:Matching";

    /// <summary>
    /// How much "unknown taste" both sides get before the score is believed (original ADR 0033). In
    /// the same units as the genre weights, <c>ln(1 + N/n)</c>, so the default 2.0 is roughly one
    /// typical genre.
    /// </summary>
    /// <remarks>
    /// It sits in the score's denominator, so it scales by itself: with a small profile it dominates
    /// (two people with one genre in common do not look like twins), with a rich one it fades into the
    /// background. Higher means "trust short profiles less"; zero brings back the behaviour from
    /// before that ADR — a perfect 1.0 from a single data point.
    /// </remarks>
    public double ConfidencePrior { get; set; } = 2.0;
}
