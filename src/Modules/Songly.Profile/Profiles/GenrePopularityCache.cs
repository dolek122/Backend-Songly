using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Songly.Profile.Profiles;

/// <summary>
/// The popularity of every genre, kept in Redis (original ADR 0032) — the data for weighting matches
/// by a genre's rarity (original ADR 0029).
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole table at once</b>, not the genres one request asks about. Genres are a closed list
/// from the schema (fifteen today), so the whole is smaller than the overhead of anything smarter.
/// More importantly, keys per subset would drift into entries of different ages, and the weights of
/// two genres in one result would come from different moments.
/// </para>
/// <para>
/// <b>Fail-open in every operation.</b> Redis failing must end in counting from the database, never
/// in an error: the cache is an optimisation, not a condition of correctness.
/// </para>
/// </remarks>
internal sealed partial class GenrePopularityCache(IConnectionMultiplexer redis, IOptions<GenrePopularityOptions> options, ILogger<GenrePopularityCache> logger)
{
    // One key for the whole installation: the numbers are global, there is nothing to split per user.
    private const string Key = "genre-popularity";

    public async Task<GenrePopularity?> ReadAsync()
    {
        try
        {
            var raw = await redis.GetDatabase().StringGetAsync(Key);
            return raw.IsNullOrEmpty ? null : JsonSerializer.Deserialize<GenrePopularity>(raw.ToString());
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or JsonException)
        {
            // A damaged entry reads as a missing one: counted from the database and overwritten.
            LogFailed("read", exception.GetType().Name);
            return null;
        }
    }

    public async Task WriteAsync(GenrePopularity snapshot)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(Key, JsonSerializer.Serialize(snapshot), options.Value.Ttl);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogFailed("write", exception.GetType().Name);
        }
    }

    /// <summary>
    /// Drops the snapshot. Called <b>after</b> the change to <c>user_genres</c> is committed, and
    /// that is a condition of correctness, not tidiness: evicted inside the transaction, a request
    /// running in parallel would recount without the uncommitted change and store the stale numbers
    /// with a fresh lifetime — keeping exactly what was meant to go for a whole hour.
    /// </summary>
    public async Task EvictAsync()
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // A failed eviction only means the snapshot lives out its lifetime.
            LogFailed("evict", exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Genre popularity cache: {Operation} failed ({Error}); counting from the database")]
    private partial void LogFailed(string operation, string error);
}

/// <summary>
/// The popularity of every genre and the denominator to weigh it with — one indivisible whole,
/// because <c>ln(1 + N/n)</c> computed from numbers of two different moments means nothing.
/// </summary>
internal sealed record GenrePopularity(Dictionary<Guid, long> Counts, long TotalUsers);
