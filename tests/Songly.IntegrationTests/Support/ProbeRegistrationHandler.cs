using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Identity.Contracts;

namespace Songly.IntegrationTests;

/// <summary>
/// Stands in for "another module" reacting to a new account, and can be told to fail for an
/// address — to show that a failing reaction takes the account down with it.
/// </summary>
internal sealed class ProbeRegistrationHandler(ProbeDbContext db) : IDomainEventHandler<UserRegistered>
{
    /// <summary>Addresses whose registration this handler fails.</summary>
    public static readonly ConcurrentDictionary<string, byte> FailFor = new(StringComparer.Ordinal);

    public async Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken)
    {
        // The event carries no address (original ADR 0046); the probe looks it up like a test would.
        var email = await db.Users.Where(user => user.Id == domainEvent.UserId).Select(user => user.Email).SingleAsync(cancellationToken);
        if (FailFor.ContainsKey(email))
        {
            throw new InvalidOperationException("A module failed to set up the new account");
        }
    }
}
