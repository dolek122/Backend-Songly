using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Songly.Identity.Http;
using Songly.Identity.Persistence;
using Songly.Identity.Security;

namespace Songly.Identity.Sessions;

/// <summary>Login, token refresh, logout and password change.</summary>
/// <remarks>
/// Logs identify accounts by id only, never by e-mail, and say nothing about an account that does
/// not exist: logs travel through systems GDPR applies to just as much as to the database
/// (original Claude.md §13), and investigating an attack needs the counter and the request id,
/// not the address.
/// </remarks>
internal sealed partial class AuthService(
    IdentityDbContext db,
    PasswordHasher passwords,
    TokenIssuer tokens,
    IdentityMetrics metrics,
    TimeProvider time,
    ILogger<AuthService> logger)
{
    /// <summary>
    /// An unknown account and a wrong password get the same 401 — and cost the same time
    /// (original ADR 0041). Identical status codes are not enough if one failure costs a query and
    /// the other a query plus bcrypt: the response time alone would tell whether an address has an
    /// account.
    /// </summary>
    public async Task<AuthTokensResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == request.Email, cancellationToken);
        if (user is null)
        {
            passwords.VerifyAgainstUnknownAccount(request.Password!);
            metrics.LoginFailed();
            LogFailedLogin("unknown account", null);
            throw new InvalidCredentialsException();
        }

        if (!passwords.Verify(request.Password!, user.PasswordHash))
        {
            metrics.LoginFailed();
            LogFailedLogin("wrong password", user.Id);
            throw new InvalidCredentialsException();
        }

        metrics.LoginSucceeded();
        LogLoggedIn(user.Id);
        return await tokens.IssueForAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Refresh-token rotation with <b>reuse detection</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rotation alone gives less than it seems: a stolen token gave the thief a valid session and
    /// the legitimate user just a silent 401 on their next refresh — nobody could notice the
    /// theft. After rotation a token exists in one copy and may be used once, so presenting a
    /// <b>revoked</b> one means a copy is out there; which side holds it is unknown. Hence
    /// <b>every</b> session of the account is revoked: the thief loses theirs, the user logs in
    /// again with the password the thief does not have.
    /// </para>
    /// <para>
    /// Revoked is not the same as expired. Expiry is just time passing and says nothing about
    /// theft — treating it as abuse would log out everywhere everyone who comes back after a month.
    /// </para>
    /// <para>
    /// Known cost, accepted in the original: a client retrying a refresh after a dropped connection
    /// looks exactly like a thief and is logged out everywhere.
    /// </para>
    /// </remarks>
    public async Task<AuthTokensResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = SecureTokens.Hash(request.RefreshToken!);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.RefreshTokenHash == hash, cancellationToken);
        if (session is null)
        {
            metrics.Refresh(RefreshOutcome.Invalid);
            throw new InvalidRefreshTokenException();
        }

        var now = time.GetUtcNow();
        if (session.Revoked)
        {
            throw await ReuseDetectedAsync(session.UserId, cancellationToken);
        }

        if (!session.IsActiveAt(now))
        {
            metrics.Refresh(RefreshOutcome.Expired);
            throw new InvalidRefreshTokenException();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Revoke only if still unrevoked, and look at how many rows that touched. Two requests
        // racing with the same token both read it as active above; without this condition both
        // would rotate it and the thief would get a second, undetected session chain. With it,
        // exactly one wins and the other is what it is — a second use of a consumed token.
        var rotated = await db.Sessions
            .Where(s => s.Id == session.Id && !s.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true).SetProperty(x => x.RevokedAt, now), cancellationToken);
        if (rotated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw await ReuseDetectedAsync(session.UserId, cancellationToken);
        }

        var response = await tokens.IssueForAsync(session.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        metrics.Refresh(RefreshOutcome.Success);
        return response;
    }

    /// <summary>
    /// Revokes every session of the account and returns the exception to throw.
    /// </summary>
    /// <remarks>
    /// The revocation is committed <b>before</b> the exception is thrown, outside any transaction
    /// the failure could roll back. That is the whole point: in the original this needed
    /// <c>noRollbackFor</c>, because without it the rollback undid the revocation and detecting a
    /// theft left nothing behind but a log line. The integration tests fail if this ever changes.
    /// </remarks>
    private async Task<RefreshTokenReuseDetectedException> ReuseDetectedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var revoked = await RevokeAllSessionsAsync(userId, cancellationToken);
        metrics.Refresh(RefreshOutcome.Reuse);
        LogReuseDetected(userId, revoked);
        return new RefreshTokenReuseDetectedException();
    }

    /// <summary>
    /// Idempotent: an unknown or already revoked token is not an error. Logging out twice (a
    /// client retrying after a dropped connection) must not look like anything.
    /// </summary>
    public async Task LogoutAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = SecureTokens.Hash(request.RefreshToken!);
        var session = await db.Sessions.SingleOrDefaultAsync(s => s.RefreshTokenHash == hash && !s.Revoked, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Requires the current password: a valid access token alone is not enough, because it may
    /// have been stolen — and this is exactly what a thief would want to do with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Revokes <b>every</b> session, including the caller's. A password change that leaves the
    /// thief a working refresh token is an illusion of security. The caller's session could not be
    /// spared anyway: the access token does not identify a session. That is also why no new token
    /// pair is returned — it would quietly undo half of the effect for this device.
    /// </para>
    /// <para>
    /// What it does <b>not</b> do: access tokens already issued stay valid until they expire
    /// (15 minutes), because JWTs are stateless by design. Revocation cuts off renewal, not the
    /// remaining minutes of validity.
    /// </para>
    /// <para>
    /// A pending password-reset link dies with the old password too: it is usually there because
    /// someone else requested a reset, and leaving it alive after the owner reacted would mean the
    /// reaction closed nothing.
    /// </para>
    /// </remarks>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidCredentialsException();

        if (!passwords.Verify(request.CurrentPassword!, user.PasswordHash))
        {
            LogFailedPasswordChange(userId);
            throw new InvalidCredentialsException();
        }

        if (passwords.Verify(request.NewPassword!, user.PasswordHash))
        {
            throw new NewPasswordSameAsCurrentException();
        }

        user.ChangePassword(passwords.Hash(request.NewPassword!));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var revoked = await RevokeAllSessionsAsync(userId, cancellationToken);
        await db.Database.ExecuteSqlAsync($"DELETE FROM password_reset_tokens WHERE user_id = {userId}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogPasswordChanged(userId, revoked);
    }

    /// <summary>
    /// One bulk update, not a loop over entities: the number of sessions per account has no upper
    /// bound, and nothing hangs on a session's lifecycle.
    /// </summary>
    private Task<int> RevokeAllSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        return db.Sessions
            .Where(s => s.UserId == userId && !s.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true).SetProperty(x => x.RevokedAt, now), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged in")]
    private partial void LogLoggedIn(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed login ({Reason}), account: {UserId}")]
    private partial void LogFailedLogin(string reason, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected, account: {UserId}; sessions revoked: {Revoked}")]
    private partial void LogReuseDetected(Guid userId, int revoked);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed password change (wrong current password), account: {UserId}")]
    private partial void LogFailedPasswordChange(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password changed for account {UserId}; sessions revoked: {Revoked}")]
    private partial void LogPasswordChanged(Guid userId, int revoked);
}
