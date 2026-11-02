using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Songly.Common.Throttling;

public static class RedisExtensions
{
    public const string ConnectionStringName = "Redis";

    /// <summary>
    /// One Redis connection for the application, for state that may be lost: interval gates now,
    /// caches and rate-limit windows later.
    /// </summary>
    public static IServiceCollection AddSonglyRedis(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(
                configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured"));

            // Start without Redis and keep reconnecting in the background: nothing kept in Redis is
            // a reason not to serve requests.
            options.AbortOnConnectFail = false;

            // Short on purpose (the original's spring.data.redis.timeout=1s). Fail-open only works if
            // failing is fast; otherwise a hung Redis is an outage after all.
            options.ConnectTimeout = 1000;
            options.SyncTimeout = 1000;
            options.AsyncTimeout = 1000;

            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<IIntervalGate, RedisIntervalGate>();
        return services;
    }
}
