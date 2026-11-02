using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Identity.Persistence;

namespace Songly.Identity.Registration;

/// <summary>
/// Deletes registrations past their expiry — once at startup, then every
/// <see cref="RegistrationOptions.CleanupInterval"/>.
/// </summary>
/// <remarks>
/// <para>
/// An expired link lets no one in, but the row still held the e-mail address and password hash of
/// someone who never created an account — sometimes of a person whose address a stranger typed in.
/// The verification mail promises that person that the registration "expires by itself" and that
/// nothing else is kept about them. Without this job that was true of the link, not of the data.
/// </para>
/// <para>
/// No retention window, unlike sessions: past its expiry a registration is dead by definition,
/// and there is nothing to investigate. A failed run is logged and retried at the next interval —
/// this is hygiene, not a critical path.
/// </para>
/// </remarks>
internal sealed partial class PendingRegistrationCleanupJob(
    IServiceScopeFactory scopes,
    IOptions<RegistrationOptions> options,
    TimeProvider time,
    ILogger<PendingRegistrationCleanupJob> logger) : BackgroundService
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
            await RemoveExpiredAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RemoveExpiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var now = time.GetUtcNow();

            // One bulk delete: nothing hangs on the lifecycle of a pending registration.
            var deleted = await db.PendingRegistrations.Where(p => p.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);
            if (deleted > 0)
            {
                LogRemoved(deleted);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} expired registrations")]
    private partial void LogRemoved(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Removing expired registrations failed")]
    private partial void LogFailed(Exception exception);
}
