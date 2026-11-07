using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Songly.Common.RateLimiting;

/// <summary>
/// A fixed-window counter in Redis, shared by the limit per address and the limit per user. They differ only in
/// what they key by and what they count — the window's mechanics and the behaviour when Redis is down must be
/// identical, so they live in one place.
/// </summary>
internal sealed partial class FixedWindowCounter(IConnectionMultiplexer redis, ILogger<FixedWindowCounter> logger)
{
    /// <summary>
    /// The count in the current window, or <see langword="null"/> when Redis does not answer — and the caller
    /// lets the request through. Rate limiting is a safeguard, not a condition of correctness: an outage of the
    /// cache must not cut everyone off from logging in or writing. The 1-second timeouts of the connection are
    /// what make that fast.
    /// </summary>
    public async Task<long?> IncrementAsync(string key, TimeSpan window)
    {
        try
        {
            var database = redis.GetDatabase();
            var count = await database.StringIncrementAsync(key);
            if (count == 1)
            {
                await database.KeyExpireAsync(key, window);
            }

            return count;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogFailOpen(exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// How long until the window ends — the client does not have to guess or retry every second. When the time is
    /// unknown, the whole window: better to make someone wait too long than too short.
    /// </summary>
    /// <remarks>
    /// A key <b>without an expiry</b> gets one here. <c>INCR</c> and <c>EXPIRE</c> are two commands, so a dropped
    /// connection or a restart between them left a counter that never expires — and that address or user cut
    /// off for good. Repairing it on a refusal costs one command, and only when someone gets a 429 anyway.
    /// </remarks>
    public async Task<long> RetryAfterSecondsAsync(string key, TimeSpan window)
    {
        try
        {
            var database = redis.GetDatabase();
            var ttl = await database.KeyTimeToLiveAsync(key);
            if (ttl is { } remaining && remaining > TimeSpan.Zero)
            {
                return Math.Max(1, (long)Math.Ceiling(remaining.TotalSeconds));
            }

            // Redis answers "no expiry" and "no key" alike here (null); the key has just been counted, so it is the former.
            if (ttl is null)
            {
                LogExpiryRestored(window);
                await database.KeyExpireAsync(key, window);
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogTtlUnknown(exception.GetType().Name);
        }

        return Math.Max(1, (long)window.TotalSeconds);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limiting disabled for this request — Redis is not answering ({Error})")]
    private partial void LogFailOpen(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A rate limit counter without an expiry — restoring the {Window} window")]
    private partial void LogExpiryRestored(TimeSpan window);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the remaining time of a rate limit window ({Error})")]
    private partial void LogTtlUnknown(string error);
}
