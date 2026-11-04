namespace Songly.NowPlaying.Status;

internal sealed class NowPlayingOptions
{
    public const string Section = "Songly:NowPlaying";

    /// <summary>
    /// How long a status read from the provider is trusted. Spotify has no webhooks, so it is polled;
    /// without the cache every read of the friends list would hit its API and its rate limits.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How long a status set by hand lives. Hours, not seconds: nothing refreshes it — it is the
    /// fallback for services without an integration (Apple Music, Tidal, YouTube Music).
    /// </summary>
    public TimeSpan ManualStatusTtl { get; set; } = TimeSpan.FromHours(3);
}
