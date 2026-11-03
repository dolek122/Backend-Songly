using Microsoft.EntityFrameworkCore;
using Songly.Common.Events;
using Songly.Common.Persistence;
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
/// Deletes the profile, the favourite genres and the avatar file of a deleted account.
/// </summary>
/// <remarks>
/// <para>
/// The rows go <b>inside</b> the deletion's transaction and before the <c>users</c> row, which both
/// tables reference. The file goes <b>after the commit</b> — the opposite phase, and the difference
/// is not cosmetic (original Claude.md §9): if the deletion rolled back after the file was gone,
/// the profile would point to a file that no longer exists — an avatar that never loads and cannot
/// be recovered. The key is read before the row goes; afterwards there is nowhere to read it from.
/// </para>
/// </remarks>
internal sealed class DeleteProfileOnAccountDeletion(ProfileDbContext db, AvatarFiles avatars, IAfterCommit afterCommit)
    : IDomainEventHandler<UserAccountDeletionRequested>
{
    public async Task HandleAsync(UserAccountDeletionRequested domainEvent, CancellationToken cancellationToken)
    {
        var avatarKey = await db.Profiles
            .Where(p => p.UserId == domainEvent.UserId)
            .Select(p => p.AvatarKey)
            .SingleOrDefaultAsync(cancellationToken);

        await db.UserGenres.Where(g => g.UserId == domainEvent.UserId).ExecuteDeleteAsync(cancellationToken);
        await db.Profiles.Where(p => p.UserId == domainEvent.UserId).ExecuteDeleteAsync(cancellationToken);

        if (avatarKey is not null)
        {
            afterCommit.Defer(token => avatars.DeleteOrphanedAsync(avatarKey, token));
        }
    }
}
