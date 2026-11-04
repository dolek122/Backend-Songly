using Microsoft.EntityFrameworkCore;
using Songly.Identity.Contracts;
using Songly.NowPlaying.Domain;
using Songly.NowPlaying.Http;
using Songly.NowPlaying.Persistence;
using Songly.NowPlaying.Providers;
using Songly.NowPlaying.Stream;
using Songly.Profile.Contracts;
using Songly.Social.Contracts;

namespace Songly.NowPlaying.Status;

/// <summary>A user's own status, the manual fallback, the friends' statuses and the visibility setting.</summary>
/// <remarks>
/// Every change of what friends may see is published to their live streams (<see cref="StatusPublisher"/>,
/// original ADR 0007) — including the ones that make a status disappear, which a friend with an open
/// stream would otherwise never learn about.
/// </remarks>
internal sealed class NowPlayingService(
    NowPlayingDbContext db,
    StatusStore statuses,
    IMusicProvider provider,
    ISocialGraph socialGraph,
    IProfileDirectory profiles,
    ISuspendedAccounts suspendedAccounts,
    StatusPublisher publisher,
    TimeProvider time)
{
    private const string DefaultManualSource = "manual";

    /// <summary>
    /// The caller's own status, <c>null</c> when nothing is playing — also when no music service is
    /// connected and nothing was set by hand, which is the state of every new account. That is a 204,
    /// not an error: the original once answered it with an undocumented 404, the mobile app took it for
    /// a failure and covered the whole screen, the manual-status form included — the only way out of
    /// that state. The owner sees their status even when it is hidden from friends.
    /// </summary>
    /// <remarks>
    /// The only endpoint that may ask the provider live (when nothing is cached), so the only one that
    /// shows the client its failures — 502 <c>MUSIC_PROVIDER_ERROR</c>, 409 when consent was taken back.
    /// The link is checked before the provider and without the cache: a cached "nothing is playing"
    /// would hide a freshly connected Spotify for the whole lifetime of the entry.
    /// </remarks>
    public async Task<NowPlayingStatusResponse?> MineAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await statuses.ManualAsync(userId) is { } manual)
        {
            return ToResponse(manual);
        }

        var (cached, status) = await statuses.ProviderAsync(userId);
        if (cached)
        {
            return status is null ? null : ToResponse(status);
        }

        if (!await db.Links.AnyAsync(l => l.UserId == userId && l.Provider == provider.Name, cancellationToken))
        {
            return null;
        }

        var fresh = await FetchAndCacheAsync(userId, cancellationToken);
        return fresh is null ? null : ToResponse(fresh);
    }

    /// <summary>Asks the provider and caches the answer — "nothing is playing" included, so that a silent user is not asked again before the entry expires.</summary>
    private async Task<StoredStatus?> FetchAndCacheAsync(Guid userId, CancellationToken cancellationToken)
    {
        var track = await provider.CurrentlyPlayingAsync(userId, cancellationToken);
        var status = track is null ? null : new StoredStatus(provider.Name, track.Title, track.Artist, track.Album, track.ImageUrl, track.Playing, time.GetUtcNow());
        await statuses.SetProviderAsync(userId, status);
        return status;
    }

    /// <summary>
    /// The universal fallback for services without an official API (original ADR 0005) — chosen
    /// deliberately over unofficial, reverse-engineered ones, which break the provider's terms and can
    /// vanish overnight. It takes precedence over the provider until it expires or is cleared.
    /// </summary>
    public async Task SetManualAsync(Guid userId, ManualNowPlayingRequest request)
    {
        var source = string.IsNullOrWhiteSpace(request.Source) ? DefaultManualSource : request.Source;
        var status = new StoredStatus(source, request.Title, request.Artist, request.Album, ImageUrl: null, Playing: true, time.GetUtcNow());
        await statuses.SetManualAsync(userId, status);
        await publisher.PublishAsync(userId, status);
    }

    /// <summary>
    /// When a fresh provider status is underneath, it becomes the current one and friends see it at the
    /// next change of track; "stopped listening" goes out only when nothing is left to show.
    /// </summary>
    public async Task ClearManualAsync(Guid userId)
    {
        await statuses.ClearManualAsync(userId);
        if ((await statuses.ProviderAsync(userId)).Status is null)
        {
            await publisher.PublishStoppedAsync(userId);
        }
    }

    /// <summary>
    /// The server-side poll's refresh: asks the provider <b>past</b> the cache — the poll fills the cache,
    /// it does not read from it — and publishes only a real change of track, or the end of playback.
    /// Without that, every run of the poll would push the same status to every stream.
    /// </summary>
    /// <remarks>Users with a manual status are skipped: it takes precedence, so asking Spotify would change nothing friends see.</remarks>
    public async Task RefreshFromProviderAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await statuses.ManualAsync(userId) is not null)
        {
            return;
        }

        var previous = (await statuses.ProviderAsync(userId)).Status;
        var fresh = await FetchAndCacheAsync(userId, cancellationToken);
        if (fresh is null)
        {
            if (previous is not null)
            {
                await publisher.PublishStoppedAsync(userId);
            }
        }
        else if (IsDifferentTrack(previous, fresh))
        {
            await publisher.PublishAsync(userId, fresh);
        }
    }

    /// <summary>
    /// Of the given users, those the poll may ask the provider about: a link, and sharing on — whoever
    /// hid their status would not have it leave the server anyway, so there is neither point nor right to
    /// ask. One query for the whole batch: the poll runs every half minute over all observed users, and
    /// most of them have no integration.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> PollableAsync(IReadOnlyCollection<Guid> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var linked = await db.Links.Where(l => l.Provider == provider.Name && candidates.Contains(l.UserId)).Select(l => l.UserId).ToListAsync(cancellationToken);
        return await SharingAsync(linked, cancellationToken);
    }

    /// <summary>By the track only — <c>fetchedAt</c> changes on every read, so comparing whole records would make every poll a change.</summary>
    private static bool IsDifferentTrack(StoredStatus? previous, StoredStatus fresh) =>
        previous is null
        || previous.Title != fresh.Title
        || previous.Artist != fresh.Artist
        || previous.Album != fresh.Album
        || previous.Playing != fresh.Playing;

    /// <summary>
    /// What friends are playing — <b>from the cache only</b>, never by asking the provider per friend:
    /// one request must not turn into N calls to Spotify and its rate limits (original Claude.md §7).
    /// Friends with nothing playing are left out; sorted by name.
    /// </summary>
    public async Task<IReadOnlyList<FriendNowPlayingResponse>> FriendsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var friends = await SharingAsync(await socialGraph.FriendIdsAsync(userId, cancellationToken), cancellationToken);
        if (friends.Count == 0)
        {
            return [];
        }

        var playing = await statuses.CurrentAsync(friends);
        var ids = playing.Keys.ToList();
        var names = await profiles.DisplayNamesAsync(ids, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(ids, cancellationToken);
        return playing
            .Select(entry => new FriendNowPlayingResponse(entry.Key, names.GetValueOrDefault(entry.Key), avatars.GetValueOrDefault(entry.Key), ToResponse(entry.Value)))
            .OrderBy(friend => friend.DisplayName is null)
            .ThenBy(friend => friend.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Visible by default: a status exists only once the user connects a service or sets one by hand — the consent is in that act, the switch is an opt-out.</summary>
    public async Task<NowPlayingSettingsResponse> SettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        new(!await db.Preferences.AnyAsync(p => p.UserId == userId && !p.ShareWithFriends, cancellationToken));

    /// <summary>The stored status is not deleted when sharing goes off: it is filtered on every way out, and the owner still sees it.</summary>
    public async Task UpdateSettingsAsync(Guid userId, bool shareWithFriends, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var preference = await db.Preferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (preference is null)
        {
            db.Preferences.Add(new NowPlayingPreference(userId, shareWithFriends, now));
        }
        else
        {
            preference.Update(shareWithFriends, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        // The flag alone is not enough: a friend with an open stream gets no push any more, so they would
        // see the last status for ever. Off sends "stopped listening"; on sends the current status, so it
        // is back on the list at once, not at the next change of track.
        if (!shareWithFriends)
        {
            await publisher.PublishStoppedAsync(userId);
        }
        else if (await statuses.CurrentAsync(userId) is { } current)
        {
            await publisher.PublishAsync(userId, current);
        }
    }

    /// <summary>
    /// Of the given users, those whose status may leave the server: they did not turn sharing off,
    /// and they are not suspended (original ADR 0049).
    /// </summary>
    /// <remarks>
    /// The suspension is here, in the same place as the visibility switch, rather than a filter of
    /// its own on every path — one place covers the friends list now, and the live stream and the
    /// poll when they come. The original first assumed a suspended user "would not refresh" their
    /// status, but the server does: a manual status lives for hours, and the poll runs on stored tokens.
    /// The opted-out are asked for, not the visible: no row means visible, so the list of opt-outs is
    /// the complete answer to "whom to leave out".
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> SharingAsync(IReadOnlyCollection<Guid> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var optedOut = (await db.Preferences.Where(p => candidates.Contains(p.UserId) && !p.ShareWithFriends).Select(p => p.UserId).ToListAsync(cancellationToken)).ToHashSet();
        var suspended = await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken);
        return candidates.Where(id => !optedOut.Contains(id) && !suspended.Contains(id)).ToList();
    }

    public static NowPlayingStatusResponse ToResponse(StoredStatus status) =>
        new(status.Provider, status.Title, status.Artist, status.Album, status.ImageUrl, status.Playing, status.FetchedAt);
}
