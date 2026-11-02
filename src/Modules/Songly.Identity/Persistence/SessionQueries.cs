using Microsoft.EntityFrameworkCore;
using Songly.Identity.Domain;

namespace Songly.Identity.Persistence;

internal static class SessionQueries
{
    /// <summary>
    /// Revokes every live session of the account (password change, password reset, detected
    /// token theft). One bulk update, not a loop over entities: the number of sessions per account
    /// has no upper bound, and nothing hangs on a session's lifecycle.
    /// </summary>
    /// <returns>How many sessions were revoked.</returns>
    public static Task<int> RevokeAllAsync(this DbSet<Session> sessions, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        sessions
            .Where(s => s.UserId == userId && !s.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true).SetProperty(x => x.RevokedAt, now), cancellationToken);
}
