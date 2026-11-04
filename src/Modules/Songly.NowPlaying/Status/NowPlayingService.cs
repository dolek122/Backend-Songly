using Microsoft.EntityFrameworkCore;
using Songly.Identity.Contracts;
using Songly.NowPlaying.Domain;
using Songly.NowPlaying.Http;
using Songly.NowPlaying.Persistence;
using Songly.Profile.Contracts;
using Songly.Social.Contracts;

namespace Songly.NowPlaying.Status;

/// <summary>A user's own status, the manual fallback, the friends' statuses and the visibility setting.</summary>
/// <remarks>
/// The original also pushes every change to the friends' live streams (original ADR 0007); that
/// arrives with the stream.
/// </remarks>
internal sealed class NowPlayingService(
    NowPlayingDbContext db,
    StatusStore statuses,
    ISocialGraph socialGraph,
    IProfileDirectory profiles,
    ISuspendedAccounts suspendedAccounts,
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
    public async Task<NowPlayingStatusResponse?> MineAsync(Guid userId) =>
        await statuses.CurrentAsync(userId) is { } status ? ToResponse(status) : null;

    /// <summary>
    /// The universal fallback for services without an official API (original ADR 0005) — chosen
    /// deliberately over unofficial, reverse-engineered ones, which break the provider's terms and can
    /// vanish overnight. It takes precedence over the provider until it expires or is cleared.
    /// </summary>
    public Task SetManualAsync(Guid userId, ManualNowPlayingRequest request)
    {
        var source = string.IsNullOrWhiteSpace(request.Source) ? DefaultManualSource : request.Source;
        return statuses.SetManualAsync(userId, new StoredStatus(source, request.Title, request.Artist, request.Album, ImageUrl: null, Playing: true, time.GetUtcNow()));
    }

    public Task ClearManualAsync(Guid userId) => statuses.ClearManualAsync(userId);

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

    /// <summary>The cached status is not deleted when sharing goes off: it is filtered on every way out, and the owner still sees it.</summary>
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

    private static NowPlayingStatusResponse ToResponse(StoredStatus status) =>
        new(status.Provider, status.Title, status.Artist, status.Album, status.ImageUrl, status.Playing, status.FetchedAt);
}
