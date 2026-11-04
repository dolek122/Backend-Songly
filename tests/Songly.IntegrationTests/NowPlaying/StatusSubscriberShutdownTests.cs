using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Songly.IntegrationTests.NowPlaying;

/// <summary>
/// Shutting down with the channel's connection already gone. Leaving the channel is the subscriber's
/// last step, and the host waits for it; when it threw, the whole stop failed with it — a deploy during
/// a Redis outage, and in the tests an instance's disposal meeting a dropped connection.
/// </summary>
public sealed class StatusSubscriberShutdownTests(SonglyApiFactory factory)
{
    [Fact]
    public async Task An_instance_stops_cleanly_when_its_redis_connection_is_gone()
    {
        var app = factory.WithSettings();
        using (var client = app.CreateClient())
        {
            // A started instance is a subscribed one: the subscriber joins the channel in its StartAsync.
            Assert.NotNull(client);
        }

        // The instance's own connection, closed under the subscriber — not the shared Redis, which every
        // test running in parallel needs.
        await app.Services.GetRequiredService<IConnectionMultiplexer>().CloseAsync(allowCommandsToComplete: false);

        await app.DisposeAsync();
    }
}
