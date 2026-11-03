using Microsoft.EntityFrameworkCore;
using Songly.Identity.Contracts;
using Songly.Identity.Persistence;

namespace Songly.Identity.Security;

internal sealed class SuspendedAccounts(IdentityDbContext db, TimeProvider time) : ISuspendedAccounts
{
    public async Task<IReadOnlySet<Guid>> SuspendedUserIdsAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        // The rule of User.IsSuspended, written out so that it runs in the database.
        var ids = await db.Users
            .Where(user => user.SuspendedAt != null && (user.SuspensionExpiresAt == null || user.SuspensionExpiresAt > now))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}
