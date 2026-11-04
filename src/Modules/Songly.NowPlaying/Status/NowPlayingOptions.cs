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

    /// <summary>How long the one-time <c>state</c> of an OAuth connection waits for the provider's redirect.</summary>
    public TimeSpan OAuthStateTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Where the callback sends the browser once OAuth is over (original ADR 0038). The result is
    /// appended as <c>result</c> (and <c>code</c> on an error), so the address must not have parameters
    /// of those names.
    /// </summary>
    public string ConnectResultUrl { get; set; } = "songly://now-playing/spotify";

    /// <summary>
    /// The AES-256 key for the providers' tokens, 32 bytes in Base64. The value in
    /// <c>appsettings.json</c> is a public development key: with it, anyone holding a database dump could
    /// decrypt every user's Spotify tokens, so it refuses to start where real secrets are required.
    /// </summary>
    public string TokenEncryptionKey { get; set; } = string.Empty;
}
