using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Songly.NowPlaying.Stream;

/// <summary>Keeps the live streams alive (original ADR 0007, stage 4) — see <see cref="StreamOptions.HeartbeatInterval"/>.</summary>
internal sealed class StreamHeartbeat(StreamHub hub, IOptions<StreamOptions> options, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.HeartbeatEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.HeartbeatInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Writing to in-memory queues only, so nothing here can fail the host.
            hub.Heartbeat();
        }
    }
}
