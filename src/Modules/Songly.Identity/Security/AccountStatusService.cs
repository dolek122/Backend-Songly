using Microsoft.EntityFrameworkCore;
using Songly.Identity.Domain;
using Songly.Identity.Persistence;

namespace Songly.Identity.Security;

/// <summary>
/// Reads the account state on every authenticated request — one query, both answers (original
/// ADR 0006, 0022). That is what makes suspending an account or revoking an admin role take effect
/// immediately, without invalidating any session.
/// </summary>
internal sealed class AccountStatusService(IdentityDbContext db, TimeProvider time)
{
    public async Task<AccountStatus> StatusOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await db.Users
            .Where(user => user.Id == userId)
            .Select(user => new { user.IsAdmin, user.SuspendedAt, user.SuspensionExpiresAt })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? AccountStatus.Unknown
            : new AccountStatus(row.IsAdmin, User.IsSuspended(row.SuspendedAt, row.SuspensionExpiresAt, time.GetUtcNow()));
    }
}
