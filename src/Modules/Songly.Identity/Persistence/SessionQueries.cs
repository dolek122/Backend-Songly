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

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> sessions that expired before
    /// <paramref name="cutoff"/>, oldest first, in one statement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A revoked session stays until its own expiry</b>, and that is a condition of theft
    /// detection, not caution (original ADR 0049). Rotation revokes the refresh token at every
    /// refresh, and <see cref="Sessions.AuthService"/> recognises a stolen copy only by finding its
    /// row revoked. The original once deleted revoked rows a week after revocation: a user coming
    /// back after eight days with the old token got a plain "unknown token", and the thief's chain
    /// of sessions lived on. A revoked but unexpired token is the same proof of a copy after a week
    /// as after a minute — so the predicate looks at <c>expires_at</c> only.
    /// </para>
    /// <para>
    /// No transaction is opened here or around the loop that calls this: each statement commits on
    /// its own, so every batch releases its locks at once. (The original had to put
    /// <c>@Transactional</c> on the repository method, not on the job, for the same reason.)
    /// </para>
    /// </remarks>
    /// <returns>How many sessions were deleted.</returns>
    public static Task<int> DeleteExpiredBatchAsync(this DbSet<Session> sessions, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken) =>
        sessions
            .Where(s => s.ExpiresAt < cutoff)
            .OrderBy(s => s.ExpiresAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
}
