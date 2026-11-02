using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Identity.Persistence;

namespace Songly.Identity.Sessions;

/// <summary>
/// Deletes sessions that can no longer authenticate anyone — once at startup, then every
/// <see cref="SessionCleanupOptions.Interval"/>.
/// </summary>
/// <remarks>
/// <para>
/// Without it the <c>sessions</c> table grows with no upper bound: every login inserts a row, every
/// refresh inserts another and revokes the previous one. With a 15-minute access token an active
/// user leaves about four dead rows an hour. That is not a correctness problem (a dead row lets no
/// one in), but a growing cost of every query on the table and of every backup.
/// </para>
/// <para>
/// One loop, so two runs never overlap: a run that takes longer than the interval is followed by
/// the next one, not raced by it. (Spring needed <c>fixedDelay</c> rather than <c>fixedRate</c> for
/// that.)
/// </para>
/// </remarks>
internal sealed partial class SessionCleanupJob(
    IServiceScopeFactory scopes,
    IOptions<SessionCleanupOptions> options,
    TimeProvider time,
    ILogger<SessionCleanupJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval, time);
        do
        {
            await RemoveDeadSessionsAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One run: batch after batch, until one comes back short or the ceiling is reached.</summary>
    /// <remarks>
    /// A failure is logged and the next run tries again — this is hygiene, not a critical path. It
    /// must not escape: an exception out of a <see cref="BackgroundService"/> <b>stops the whole
    /// application</b> by default, so a database unavailable for a moment at 3 a.m. would take the
    /// API down with it.
    /// </remarks>
    internal async Task RemoveDeadSessionsAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = time.GetUtcNow() - settings.Retention;
        var total = 0;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            for (var batch = 0; batch < settings.MaxBatchesPerRun; batch++)
            {
                var deleted = await db.Sessions.DeleteExpiredBatchAsync(cutoff, settings.BatchSize, cancellationToken);
                total += deleted;

                // A short batch means nothing is left; another query would only load the database.
                if (deleted < settings.BatchSize)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(total, exception);
            return;
        }

        if (total > 0)
        {
            LogRemoved(total, settings.Retention);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} sessions expired for longer than {Retention}")]
    private partial void LogRemoved(int count, TimeSpan retention);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removing dead sessions failed after {Count} were removed")]
    private partial void LogFailed(int count, Exception exception);
}
