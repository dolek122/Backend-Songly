using System.Text.Json;
using Microsoft.Extensions.Options;
using Songly.NowPlaying.Status;
using StackExchange.Redis;

namespace Songly.NowPlaying.Stream;

/// <summary>
/// A user's status changed — sent through a Redis channel to <b>every</b> instance (original ADR 0007,
/// stage 2), each of which pushes it to its own local streams of the user's friends. <c>Status</c>
/// <c>null</c> means "stopped listening".
/// </summary>
/// <remarks>
/// A contract <b>between instances</b>: during a rolling deploy old and new ones listen on the same
/// channel, so adding fields is safe, removing or changing their meaning is not.
/// </remarks>
internal sealed record StatusChanged(Guid UserId, StoredStatus? Status);

/// <summary>
/// Publishes status changes to the Redis channel. The publisher does not take a shortcut for itself:
/// its own instance receives the same message as the others, so there is exactly one delivery path
/// to test and maintain.
/// </summary>
/// <remarks>
/// Every publication also keeps the marker "we announced this user is playing" — here, the one place
/// all publications go through, so the marker cannot drift from what the clients see. It tells "the
/// status expired" from "never played anything" (<see cref="PublishStoppedIfExpiredAsync"/>).
/// </remarks>
internal sealed class StatusPublisher(IConnectionMultiplexer redis, StatusStore statuses, IOptions<StreamOptions> options)
{
    public static readonly RedisChannel Channel = RedisChannel.Literal("now-playing:status-changed");

    private const string AnnouncedKeyPrefix = "now-playing:announced:";

    public async Task PublishAsync(Guid userId, StoredStatus? status)
    {
        var database = redis.GetDatabase();
        await database.PublishAsync(Channel, JsonSerializer.Serialize(new StatusChanged(userId, status)));
        if (status is null)
        {
            await database.KeyDeleteAsync(AnnouncedKeyPrefix + userId);
        }
        else
        {
            await database.StringSetAsync(AnnouncedKeyPrefix + userId, "1", options.Value.AnnouncedTtl);
        }
    }

    /// <summary>
    /// "Stopped listening" — an empty status instead of a track. Without it the client would have to guess
    /// the end of playback from the age of <c>fetchedAt</c>, and would show a friend as playing long after
    /// they turned the music off.
    /// </summary>
    public Task PublishStoppedAsync(Guid userId) => PublishAsync(userId, status: null);

    /// <summary>
    /// "Stopped listening" for a status that vanished by itself — its lifetime ran out, nobody did
    /// anything. Without it a manual status (hours of lifetime) would hang on a friend's screen for
    /// ever: its owner is not polled, so nothing notices it is gone. Redis does not say when a key
    /// expires; instead of keyspace notifications (server configuration, one more channel to keep), the
    /// poll checks it as it goes over the observed users anyway.
    /// </summary>
    /// <remarks>Two instances may both publish; harmless — the client removes an entry it no longer has.</remarks>
    public async Task PublishStoppedIfExpiredAsync(Guid userId)
    {
        if (await redis.GetDatabase().KeyExistsAsync(AnnouncedKeyPrefix + userId) && await statuses.CurrentAsync(userId) is null)
        {
            await PublishStoppedAsync(userId);
        }
    }
}
