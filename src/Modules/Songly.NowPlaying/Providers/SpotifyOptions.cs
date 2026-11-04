namespace Songly.NowPlaying.Providers;

internal sealed class SpotifyOptions
{
    public const string Section = "Songly:NowPlaying:Spotify";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Our public callback, registered with Spotify — it must match the registration character for character.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    public string AuthorizeUrl { get; set; } = "https://accounts.spotify.com/authorize";

    public string TokenUrl { get; set; } = "https://accounts.spotify.com/api/token";

    public string ApiBaseUrl { get; set; } = "https://api.spotify.com/v1";

    /// <summary>
    /// Set explicitly: the calls run from the server-side poll too, and a hung connection to Spotify
    /// would hold the poll of every observed user — one provider's outage would become the outage of
    /// the whole presence feature.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
