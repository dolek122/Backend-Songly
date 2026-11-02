using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Identity.Contracts;

namespace Songly.IntegrationTests;

/// <summary>
/// Stands in for "another module" reacting to an account deletion, with its own DbContext — the
/// way every module with per-user data will. It records what it saw, and can be told to fail.
/// </summary>
internal sealed class ProbeDeletionHandler(ProbeDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    /// <summary>Whether the <c>users</c> row still existed when the handler ran, per account.</summary>
    public static readonly ConcurrentDictionary<Guid, bool> UserRowPresentWhenHandled = new();

    /// <summary>Accounts for which the handler deletes something of its own and then fails.</summary>
    public static readonly ConcurrentDictionary<Guid, byte> FailFor = new();

    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        UserRowPresentWhenHandled[domainEvent.UserId] = await db.Users.AnyAsync(user => user.Id == domainEvent.UserId, cancellationToken);

        if (FailFor.ContainsKey(domainEvent.UserId))
        {
            // A write through this module's own context, then a failure: both must vanish together.
            await db.Database.ExecuteSqlAsync($"DELETE FROM sessions WHERE user_id = {domainEvent.UserId}", cancellationToken);
            throw new InvalidOperationException("A module failed to delete its data");
        }
    }
}
