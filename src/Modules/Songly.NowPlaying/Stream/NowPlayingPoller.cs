using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.NowPlaying.Providers;
using Songly.NowPlaying.Status;
using Songly.Social.Contracts;
using StackExchange.Redis;

namespace Songly.NowPlaying.Stream;

/// <summary>
/// The server-side poll of the provider (original ADR 0007, stage 1b). Spotify has no webhooks, so
/// somebody has to ask — one job per instance instead of N clients, which keeps the traffic to Spotify
/// and its rate limits in one place (original Claude.md §7). The shape of ADR 0007: once at startup,
/// then every interval, never overlapping, a scope per run and nothing escaping it.
/// </summary>
/// <remarks>
/// Only users <b>somebody is watching</b> are asked about — the friends of users with an open stream on
/// this instance. Nobody watching, nobody asked: without that the poll would grow with the number of
/// accounts, not with the number of real viewers.
/// </remarks>
internal sealed partial class NowPlayingPoller(
    StreamHub hub,
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopes,
    IOptions<PollOptions> options,
    TimeProvider time,
    ILogger<NowPlayingPoller> logger) : BackgroundService
{
    private const string LockKeyPrefix = "now-playing:poll-lock:";

    /// <summary>
    /// Spotify's rate limit is per client ID — shared by all our instances and all users — so the marker
    /// is one Redis key, not process memory: the instance that got the 429 silences the others too. The
    /// key's lifetime <i>is</i> the backoff.
    /// </summary>
    private const string BackoffKey = "now-playing:provider-backoff";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRunFailed(exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var observed = await ObservedAsync(services.GetRequiredService<ISocialGraph>(), cancellationToken);
        if (observed.Count == 0)
        {
            return;
        }

        var nowPlaying = services.GetRequiredService<NowPlayingService>();
        foreach (var userId in await PollableAsync(nowPlaying, observed, cancellationToken))
        {
            try
            {
                // With many instances the same observed user is on every one's list — the lock makes exactly
                // one of them ask in a cycle; the channel carries the change to the others. Inside the try: a
                // lock not taken is normal, a Redis error is not, and must not take the rest of the run.
                if (!await redis.GetDatabase().StringSetAsync(LockKeyPrefix + userId, "1", options.Value.LockTtl, When.NotExists))
                {
                    continue;
                }

                await nowPlaying.RefreshFromProviderAsync(userId, cancellationToken);
            }
            catch (SpotifyApiException exception) when (exception.IsRateLimited)
            {
                // The limit is shared by the whole client: the next users in this run would get the same
                // 429 and only deepen the penalty.
                await BackOffAsync(exception.RetryAfter ?? options.Value.ProviderBackoff);
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Consent taken back, the network, Redis — one user must not take the whole run.
                LogRefreshFailed(userId, exception);
            }
        }

        // Over <b>all</b> observed users, not only those with a provider: a manual status expires by
        // itself, and its owner has nothing to poll — this is the only way to notice it stopped.
        var publisher = services.GetRequiredService<StatusPublisher>();
        foreach (var userId in observed)
        {
            try
            {
                await publisher.PublishStoppedIfExpiredAsync(userId);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogExpiryCheckFailed(userId, exception);
            }
        }
    }

    /// <summary>The observed: the friends of this instance's viewers. One query to the graph per viewer — there are as many as hold a stream here.</summary>
    private async Task<IReadOnlyCollection<Guid>> ObservedAsync(ISocialGraph socialGraph, CancellationToken cancellationToken)
    {
        var observed = new HashSet<Guid>();
        foreach (var viewerId in hub.ConnectedUserIds())
        {
            observed.UnionWith(await socialGraph.FriendIdsAsync(viewerId, cancellationToken));
        }

        return observed;
    }

    /// <summary>
    /// A failure here must not take the run: the expiry sweep after it does not depend on the provider.
    /// An empty list during the backoff silences only the provider — manual statuses expire anyway.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> PollableAsync(NowPlayingService nowPlaying, IReadOnlyCollection<Guid> observed, CancellationToken cancellationToken)
    {
        try
        {
            return await redis.GetDatabase().KeyExistsAsync(BackoffKey) ? [] : await nowPlaying.PollableAsync(observed, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPollableFailed(exception);
            return [];
        }
    }

    /// <summary>The provider's <c>Retry-After</c> over our own guess — only it knows when its window renews.</summary>
    private async Task BackOffAsync(TimeSpan backoff)
    {
        LogBackingOff(backoff);
        try
        {
            await redis.GetDatabase().StringSetAsync(BackoffKey, "1", backoff);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // The run stops anyway; the next one simply tries again.
            LogBackoffNotSaved(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Now-playing poll run failed")]
    private partial void LogRunFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling the status of {UserId} failed")]
    private partial void LogRefreshFailed(Guid userId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Checking the status expiry of {UserId} failed")]
    private partial void LogExpiryCheckFailed(Guid userId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not tell whom to ask the provider about")]
    private partial void LogPollableFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The provider rate-limited us; polling paused for {Backoff}")]
    private partial void LogBackingOff(TimeSpan backoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the provider backoff")]
    private partial void LogBackoffNotSaved(Exception exception);
}
