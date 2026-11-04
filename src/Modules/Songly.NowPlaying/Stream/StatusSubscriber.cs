using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Songly.NowPlaying.Stream;

/// <summary>
/// Listens on the status channel on <b>every</b> instance (original ADR 0007, stage 2) and hands each
/// change to the broadcaster, which decides whom to push it to locally.
/// </summary>
/// <remarks>
/// Messages are handled one after another (the channel's queue). One that cannot be read — another
/// shape after a deploy — is logged and dropped: presence is ephemeral, a lost change is harmless, and
/// an exception here would only litter the log. A DI scope per message: the broadcaster asks the
/// database through scoped services.
/// </remarks>
internal sealed partial class StatusSubscriber(IConnectionMultiplexer redis, IServiceScopeFactory scopes, ILogger<StatusSubscriber> logger) : IHostedService
{
    private ChannelMessageQueue? _queue;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _queue = await redis.GetSubscriber().SubscribeAsync(StatusPublisher.Channel);
            _queue.OnMessage(HandleAsync);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // Presence is not a reason to keep the whole API down; the streams simply get no pushes.
            LogSubscriptionFailed(exception);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_queue is not null)
        {
            await _queue.UnsubscribeAsync();
        }
    }

    private async Task HandleAsync(ChannelMessage message)
    {
        try
        {
            var change = JsonSerializer.Deserialize<StatusChanged>(message.Message.ToString()) ?? throw new JsonException("empty message");
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<StreamBroadcaster>().BroadcastAsync(change, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogMessageDropped(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not subscribe to the now-playing channel; live streams get no pushes")]
    private partial void LogSubscriptionFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped a now-playing change that could not be delivered")]
    private partial void LogMessageDropped(Exception exception);
}
