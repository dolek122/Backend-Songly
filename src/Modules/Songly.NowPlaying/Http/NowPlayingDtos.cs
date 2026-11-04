using Songly.Common.Validation;

namespace Songly.NowPlaying.Http;

/// <summary><c>source</c> is the name of the service the user typed (Apple Music, Tidal…), optional.</summary>
internal sealed record ManualNowPlayingRequest
{
    [NotBlank]
    [Size(0, 200)]
    public string? Title { get; init; }

    [Size(0, 200)]
    public string? Artist { get; init; }

    [Size(0, 200)]
    public string? Album { get; init; }

    [Size(0, 60)]
    public string? Source { get; init; }
}

/// <summary>Where to send the user's browser to give consent at the provider.</summary>
internal sealed record ConnectResponse(string AuthorizeUrl);

internal sealed record UpdateNowPlayingSettingsRequest
{
    [NotNull]
    public bool? ShareWithFriends { get; init; }
}

/// <summary><c>shareWithFriends=false</c>: the status reaches neither the friends list, nor the live stream, nor even the server-side poll.</summary>
internal sealed record NowPlayingSettingsResponse(bool ShareWithFriends);

internal sealed record NowPlayingStatusResponse(
    string Provider,
    string? Title,
    string? Artist,
    string? Album,
    string? ImageUrl,
    bool Playing,
    DateTimeOffset FetchedAt);

/// <summary>
/// A friend's status with their display data, so the client does not have to fetch the profile
/// separately. On the live stream <c>status</c> <c>null</c> means "stopped listening" — remove the friend
/// from the list of those playing; on <c>GET /friends</c> it is never null.
/// </summary>
internal sealed record FriendNowPlayingResponse(Guid UserId, string? DisplayName, string? AvatarUrl, NowPlayingStatusResponse? Status);
