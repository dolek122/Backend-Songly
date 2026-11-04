namespace Songly.NowPlaying.Providers;

/// <summary>"What is playing now", normalised — independent of the provider.</summary>
internal sealed record NowPlayingTrack(string? Title, string? Artist, string? Album, string? ImageUrl, bool Playing);

/// <summary>
/// The boundary for many music services (original Claude.md §7): adding one (should an official API
/// appear for Apple Music or Tidal) is a new implementation, without touching the rest of the module.
/// </summary>
internal interface IMusicProvider
{
    string Name { get; }

    string AuthorizeUrl(string state);

    Task ConnectAsync(Guid userId, string authorizationCode, CancellationToken cancellationToken);

    /// <summary><c>null</c> when nothing is playing.</summary>
    Task<NowPlayingTrack?> CurrentlyPlayingAsync(Guid userId, CancellationToken cancellationToken);
}
