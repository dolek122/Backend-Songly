namespace Songly.Profile.Profiles;

internal sealed class GenrePopularityOptions
{
    public const string Section = "Songly:Profile:GenrePopularity";

    /// <summary>
    /// How long the cached snapshot lives (original ADR 0032). An hour is long for a cache, and that
    /// is the point: popularity changes only when someone edits their favourite genres, and every
    /// such change evicts the snapshot. The lifetime is not what keeps it fresh — it is a safety net
    /// for changes that went around the application: a migration, a manual <c>UPDATE</c>, a restored
    /// backup.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(1);
}
