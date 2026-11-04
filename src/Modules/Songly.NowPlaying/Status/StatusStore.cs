using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Songly.NowPlaying.Status;

/// <summary>A status as stored — the same shape whether it came from a provider or was set by hand.</summary>
internal sealed record StoredStatus(string Provider, string? Title, string? Artist, string? Album, string? ImageUrl, bool Playing, DateTimeOffset FetchedAt);

/// <summary>
/// "What is playing now", kept in Redis (original Claude.md §4, §7): it changes every few seconds,
/// and writing that into Postgres would bury the database in writes. Everything here may be lost on a
/// restart — a lost status is simply "nothing playing" until the next track.
/// </summary>
/// <remarks>
/// <para>
/// Two keys per user. The <b>manual</b> status (the fallback for services without an integration)
/// lives in a key of its own with a longer lifetime and <b>takes precedence</b> over the provider's
/// until it expires or is cleared. The provider's key also remembers "nothing is playing", so that
/// a silent user is not asked about again before the entry expires.
/// </para>
/// <para>
/// Unlike the caches elsewhere, this is not fail-open: Redis is where the status is, not a copy of
/// it, so a request about the status cannot be answered without it.
/// </para>
/// </remarks>
internal sealed class StatusStore(IConnectionMultiplexer redis, IOptions<NowPlayingOptions> options)
{
    private const string ProviderKeyPrefix = "now-playing:status:";
    private const string ManualKeyPrefix = "now-playing:manual:";
    private const string NothingPlaying = "NOTHING_PLAYING";

    private IDatabase Database => redis.GetDatabase();

    /// <summary>The current status: manual first, then the provider's; <c>null</c> when nothing is playing.</summary>
    public async Task<StoredStatus?> CurrentAsync(Guid userId) =>
        Parse(await Database.StringGetAsync(ManualKey(userId))) ?? Parse(await Database.StringGetAsync(ProviderKey(userId)));

    /// <summary>
    /// The statuses of many users in two round trips instead of two per person — a friends list can
    /// have hundreds of entries. Users with nothing playing are absent.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, StoredStatus>> CurrentAsync(IReadOnlyList<Guid> userIds)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, StoredStatus>();
        }

        var manual = await Database.StringGetAsync(userIds.Select(id => (RedisKey)ManualKey(id)).ToArray());
        var provider = await Database.StringGetAsync(userIds.Select(id => (RedisKey)ProviderKey(id)).ToArray());
        var statuses = new Dictionary<Guid, StoredStatus>();
        for (var i = 0; i < userIds.Count; i++)
        {
            if ((Parse(manual[i]) ?? Parse(provider[i])) is { } status)
            {
                statuses[userIds[i]] = status;
            }
        }

        return statuses;
    }

    public Task SetManualAsync(Guid userId, StoredStatus status) =>
        Database.StringSetAsync(ManualKey(userId), JsonSerializer.Serialize(status), options.Value.ManualStatusTtl);

    public Task ClearManualAsync(Guid userId) => Database.KeyDeleteAsync(ManualKey(userId));

    /// <summary>Both keys of a deleted account. They would expire on their own; this only makes it immediate.</summary>
    public Task ForgetAsync(Guid userId) => Database.KeyDeleteAsync([ManualKey(userId), ProviderKey(userId)]);

    private static string ManualKey(Guid userId) => ManualKeyPrefix + userId;

    private static string ProviderKey(Guid userId) => ProviderKeyPrefix + userId;

    private static StoredStatus? Parse(RedisValue raw) =>
        raw.IsNullOrEmpty || raw == NothingPlaying ? null : JsonSerializer.Deserialize<StoredStatus>(raw.ToString());
}
