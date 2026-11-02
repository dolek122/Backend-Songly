using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Songly.Identity.Persistence;
using Songly.Identity.Sessions;

namespace Songly.Identity.Tests;

/// <summary>
/// Ported from the original <c>SessionCleanupJobTest.survivesDatabaseFailure</c>. Which rows the
/// job deletes, in what batches, is tested against Postgres in <c>SessionCleanupTests</c>.
/// </summary>
public sealed class SessionCleanupJobTests
{
    /// <summary>
    /// The stakes are higher than in the original: Spring logs an exception out of a scheduled task
    /// and runs the next one, while an exception out of a <c>BackgroundService</c> stops the whole
    /// application. A database unavailable for a moment must cost one skipped cleanup, not the API.
    /// </summary>
    [Fact]
    public async Task A_failed_run_does_not_escape()
    {
        var services = new ServiceCollection()
            .AddScoped<IdentityDbContext>(_ => throw new InvalidOperationException("The database is down"))
            .BuildServiceProvider();
        var job = new SessionCleanupJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SessionCleanupOptions()),
            TimeProvider.System,
            NullLogger<SessionCleanupJob>.Instance);

        await job.RemoveDeadSessionsAsync(TestContext.Current.CancellationToken);
    }
}
