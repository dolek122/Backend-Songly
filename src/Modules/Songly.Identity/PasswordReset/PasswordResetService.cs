using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Identity.Domain;
using Songly.Identity.Http;
using Songly.Identity.Persistence;
using Songly.Identity.Security;

namespace Songly.Identity.PasswordReset;

/// <summary>
/// Password reset for a user who does not remember the password (original ADR 0008).
/// </summary>
/// <remarks>
/// <para>
/// Without it, protection against account takeover ended in losing the account for good: refresh
/// token reuse detection logs out every device on the assumption that "the user comes back with
/// the password the thief does not have" — false for anyone who forgot that password.
/// </para>
/// <para>
/// <b>Neither endpoint reveals whether an account exists.</b> Requesting a reset always ends the
/// same way (202), and a refused reset always with the same error code. The endpoint is public, so
/// any difference in the answer would be a free list of accounts.
/// </para>
/// </remarks>
internal sealed partial class PasswordResetService(
    IdentityDbContext db,
    PasswordHasher passwords,
    PasswordResetMailer mailer,
    IOptions<PasswordResetOptions> options,
    IdentityMetrics metrics,
    TimeProvider time,
    ILogger<PasswordResetService> logger)
{
    /// <summary>
    /// Requests a reset link. Returns nothing, and <b>always</b> the same way — whether the account
    /// exists or not, and whether the resend interval just silenced the request.
    /// </summary>
    /// <remarks>
    /// One live token per account: a new request deletes the previous one. Otherwise every further
    /// "I did not get the e-mail" would leave another valid key to the account in circulation, with
    /// no way for the user to invalidate it.
    /// </remarks>
    public async Task RequestResetAsync(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        metrics.PasswordReset("requested");
        var account = await db.Users
            .Where(user => user.Email == request.Email)
            .Select(user => new { user.Id, user.Email })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            // Deliberately no log line and no separate metric: either would be a record of "there is
            // no account at this address" — the same oracle, moved from the response into a system
            // that GDPR applies to just as well.
            return;
        }

        var settings = options.Value;
        var now = time.GetUtcNow();
        var lastIssuedAt = await db.PasswordResetTokens
            .Where(token => token.UserId == account.Id)
            .MaxAsync(token => (DateTimeOffset?)token.CreatedAt, cancellationToken);
        if (lastIssuedAt > now - settings.ResendInterval)
        {
            metrics.PasswordReset("throttled");
            LogThrottled(account.Id);
            return;
        }

        var token = SecureTokens.Generate();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.PasswordResetTokens.Where(t => t.UserId == account.Id).ExecuteDeleteAsync(cancellationToken);
            db.PasswordResetTokens.Add(new PasswordResetToken(account.Id, SecureTokens.Hash(token), now, settings.Ttl));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        LogIssued(account.Id);
        await mailer.SendResetLinkAsync(account.Id, account.Email, token, cancellationToken);
    }

    /// <summary>
    /// Sets the new password from the token in the link.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The effect is deliberately that of a password change: <b>every session dies</b>. A reset is
    /// what one does after losing control of an account, so leaving the sessions from before it
    /// alive would undo the whole point. Every other reset token of the account goes too — a link
    /// requested in parallel must not open the account after the owner got it back.
    /// </para>
    /// <para>
    /// The token is consumed with a conditional delete, and the number of deleted rows decides. Two
    /// requests with the same link would otherwise both pass the lookup and both set a password;
    /// this way exactly one of them gets the token.
    /// </para>
    /// </remarks>
    public async Task ResetPasswordAsync(PasswordResetConfirmRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = SecureTokens.Hash(request.Token!);
        var now = time.GetUtcNow();
        var userId = await db.PasswordResetTokens
            .Where(token => token.TokenHash == tokenHash && token.ExpiresAt > now)
            .Select(token => (Guid?)token.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (userId is null)
        {
            throw InvalidToken();
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        user.ChangePassword(passwords.Hash(request.NewPassword!));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var consumed = await db.PasswordResetTokens.Where(token => token.TokenHash == tokenHash).ExecuteDeleteAsync(cancellationToken);
        if (consumed == 0)
        {
            // A concurrent request with the same link was first.
            throw InvalidToken();
        }

        await db.SaveChangesAsync(cancellationToken);
        var revoked = await db.Sessions.RevokeAllAsync(user.Id, now, cancellationToken);
        await db.PasswordResetTokens.Where(token => token.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        metrics.PasswordReset("success");
        LogReset(user.Id, revoked);
    }

    private InvalidPasswordResetTokenException InvalidToken()
    {
        metrics.PasswordReset("invalid-token");
        LogInvalidToken();
        return new InvalidPasswordResetTokenException();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset request silenced by the resend interval, account: {UserId}")]
    private partial void LogThrottled(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Issued a password reset token for account {UserId}")]
    private partial void LogIssued(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Password reset for account {UserId}; sessions revoked: {Revoked}")]
    private partial void LogReset(Guid userId, int revoked);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a password reset token")]
    private partial void LogInvalidToken();
}
