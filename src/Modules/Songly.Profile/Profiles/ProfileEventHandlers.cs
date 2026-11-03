using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Identity.Contracts;
using Songly.Profile.Persistence;

namespace Songly.Profile.Profiles;

/// <summary>A new account gets its default profile (User 1—1 Profile), in the account's transaction.</summary>
internal sealed class CreateProfileOnRegistration(ProfileService profiles) : IDomainEventHandler<UserRegistered>
{
    public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken) =>
        profiles.CreateDefaultAsync(domainEvent.UserId, cancellationToken);
}

/// <summary>
/// Deletes the profile and the favourite genres of a deleted account — inside the deletion's
/// transaction and before the <c>users</c> row goes, which both tables reference.
/// </summary>
internal sealed class DeleteProfileOnAccountDeletion(ProfileDbContext db) : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        await db.UserGenres.Where(g => g.UserId == domainEvent.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Profiles.Where(p => p.UserId == domainEvent.UserId).ExecuteDeleteAsync(cancellationToken);
    }
}
