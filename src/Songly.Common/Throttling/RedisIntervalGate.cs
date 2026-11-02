using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Songly.Common.Throttling;

/// <summary>
/// The interval lives in Redis (<c>SET key NX PX interval</c>): it is shared by every instance and
/// it is state that may be lost on a restart.
/// </summary>
/// <remarks>
/// <b>Fail-open</b>, like the rate limiter of the original (Claude.md §9): when Redis does not answer,
/// the call passes. These gates protect someone's inbox from flooding; they are not a condition of
/// correctness, and an outage of the cache must not silence a message that should have gone out.
/// The connection's 1-second timeouts (see <see cref="RedisExtensions"/>) are what make fail-open
/// work — with the client's defaults a hung Redis would hold every request for seconds.
/// </remarks>
internal sealed partial class RedisIntervalGate(IConnectionMultiplexer redis, ILogger<RedisIntervalGate> logger) : IIntervalGate
{
    public async Task<bool> TryPassAsync(string key, TimeSpan interval)
    {
        // Redis rejects a zero expiry; a zero interval simply means "no gate".
        if (interval <= TimeSpan.Zero)
        {
            return true;
        }

        try
        {
            return await redis.GetDatabase().StringSetAsync(key, "1", interval, When.NotExists);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogFailOpen(exception.GetType().Name);
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Interval gate disabled for this call — Redis is not answering ({Error})")]
    private partial void LogFailOpen(string error);
}
