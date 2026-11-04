using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Songly.Identity.Contracts;
using Songly.NowPlaying.Http;
using Songly.NowPlaying.Status;
using Songly.Profile.Contracts;
using Songly.Social.Contracts;

namespace Songly.NowPlaying.Stream;

/// <summary>
/// From a status change received on the channel to the open streams of the user's <b>friends</b>
/// (original ADR 0007, stages 1–2). Friends counted <b>at the moment of the change</b> — the relation may
/// have changed since the stream opened — never strangers or blocked users: the graph returns accepted
/// friendships only.
/// </summary>
internal sealed class StreamBroadcaster(
    StreamHub hub,
    ISocialGraph socialGraph,
    IProfileDirectory profiles,
    NowPlayingService nowPlaying,
    ISuspendedAccounts suspendedAccounts,
    IOptions<JsonOptions> json)
{
    public async Task BroadcastAsync(StatusChanged change, CancellationToken cancellationToken)
    {
        // The fan-out brings every change to every instance, also to one where nobody has a stream —
        // there is no point asking the graph there.
        if (!hub.HasAnySubscriber())
        {
            return;
        }

        var subjectId = change.UserId;

        // Visibility is checked on every push, not only when the stream opens: the user may have turned
        // sharing off a second ago. "Stopped listening" always goes through — it reveals nothing, only
        // removes an entry, and it is exactly what turning sharing off sends.
        if (change.Status is not null && (await nowPlaying.SharingAsync([subjectId], cancellationToken)).Count == 0)
        {
            return;
        }

        var listening = (await socialGraph.FriendIdsAsync(subjectId, cancellationToken)).Where(hub.HasSubscribers).ToList();

        // "Stopped listening" also goes to whoever had the subject as a friend when their stream opened. A
        // deleted account sends it after its friendships are gone, so the friends counted now would be
        // nobody, and the ghost would stay on their screens until a reconnect. It reveals nothing, so a
        // wider circle costs no privacy; a status still goes to current friends only.
        if (change.Status is null)
        {
            listening = [.. listening.Union(hub.ViewersWhoKnew(subjectId))];
        }

        if (listening.Count == 0)
        {
            return;
        }

        // A suspended recipient with an open stream gets nothing any more (original ADR 0049): a stream
        // lives up to 30 minutes, and a suspension works at once — the role cuts off new requests, not an
        // open connection.
        var suspended = await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken);
        var recipients = listening.Where(id => !suspended.Contains(id)).ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        var names = await profiles.DisplayNamesAsync([subjectId], cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync([subjectId], cancellationToken);
        var payload = new FriendNowPlayingResponse(
            subjectId,
            names.GetValueOrDefault(subjectId),
            avatars.GetValueOrDefault(subjectId),
            change.Status is { } status ? NowPlayingService.ToResponse(status) : null);
        var frame = $"event: now-playing\ndata: {JsonSerializer.Serialize(payload, json.Value.SerializerOptions)}\n\n";
        foreach (var friendId in recipients)
        {
            hub.Push(friendId, frame);
        }
    }
}
