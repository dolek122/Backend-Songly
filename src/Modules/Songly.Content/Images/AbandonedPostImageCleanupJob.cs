using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Songly.Content.Images;

/// <summary>Removes images uploaded but never attached to a post (original ADR 0048), once at startup and then every interval (ADR 0007).</summary>
internal sealed partial class AbandonedPostImageCleanupJob(
    IServiceScopeFactory scopes,
    IOptions<PostImageOptions> options,
    TimeProvider time,
    ILogger<AbandonedPostImageCleanupJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.CleanupEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.CleanupInterval, time);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Hygiene, not a critical path: a failure — the storage stopping the run, too — is logged and the next run carries on.</summary>
    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var removed = await scope.ServiceProvider.GetRequiredService<PostImageUploads>().RemoveAbandonedAsync(cancellationToken);
            if (removed > 0)
            {
                LogRemoved(removed);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} abandoned post images")]
    private partial void LogRemoved(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removing abandoned post images failed")]
    private partial void LogFailed(Exception exception);
}
