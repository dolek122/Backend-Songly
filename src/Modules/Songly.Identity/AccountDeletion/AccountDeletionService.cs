using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Common.Persistence;
using Songly.Identity.Domain;
using Songly.Identity.Persistence;
using Songly.Identity.Security;
using Songly.Identity.Sessions;

namespace Songly.Identity.AccountDeletion;

/// <summary>
/// The right to erasure (GDPR, original Claude.md §9): a hard delete of the account and all its
/// data, not a flag. Two ways in, one deletion (<see cref="AccountEraser"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>In the app</b> the password is required again: a valid access token alone is not enough for an
/// irreversible action — it may have been stolen.
/// </para>
/// <para>
/// <b>Without the app</b> (original ADR 0043): Google Play requires that deletion can be requested
/// outside the app — whoever uninstalled it must not have to install it again. A password on the web
/// page would not do (whoever forgot it would need a reset, and the reset link opens the app), so the
/// proof is access to the mailbox — the same proof a password reset already accepts for the whole
/// account. The request never reveals whether an account exists: same shape as a password reset.
/// </para>
/// <para>
/// A suspension blocks neither way: a sanction must not take away a GDPR right.
/// </para>
/// </remarks>
internal sealed partial class AccountDeletionService(
    IdentityDbContext db,
    PasswordHasher passwords,
    AccountEraser eraser,
    ModuleTransaction transaction,
    AccountDeletionMailer mailer,
    IOptions<AccountDeletionOptions> options,
    IdentityMetrics metrics,
    TimeProvider time,
    ILogger<AccountDeletionService> logger)
{
    public async Task DeleteOwnAccountAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidCredentialsException();
        if (!passwords.Verify(password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        // Irreversible and across every module — if anyone ever asks "what happened to this
        // account", this line is the only trace left.
        LogDeletingOnRequest(userId);
        await EraseAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Requests the link. Always the same outcome; nothing logged or counted for an unknown
    /// address; one live link per account; one message per resend interval.
    /// </summary>
    public async Task RequestDeletionLinkAsync(string email, CancellationToken cancellationToken)
    {
        metrics.AccountDeletion("requested");
        var account = await db.Users
            .Where(user => user.Email == email)
            .Select(user => new { user.Id, user.Email })
            .SingleOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return;
        }

        var settings = options.Value;
        var now = time.GetUtcNow();
        var lastIssuedAt = await db.AccountDeletionTokens
            .Where(token => token.UserId == account.Id)
            .MaxAsync(token => (DateTimeOffset?)token.CreatedAt, cancellationToken);
        if (lastIssuedAt > now - settings.ResendInterval)
        {
            metrics.AccountDeletion("throttled");
            return;
        }

        var token = SecureTokens.Generate();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.AccountDeletionTokens.Where(t => t.UserId == account.Id).ExecuteDeleteAsync(cancellationToken);
            db.AccountDeletionTokens.Add(new AccountDeletionToken(account.Id, SecureTokens.Hash(token), now, settings.Ttl));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        LogLinkIssued(account.Id);
        await mailer.SendConfirmationLinkAsync(account.Id, account.Email, token, cancellationToken);
    }

    /// <summary>
    /// Whether the link still works — the confirmation page shows the button only then. It tells
    /// this only to the holder of the token, who could delete the account anyway.
    /// </summary>
    public async Task<bool> IsValidAsync(string? token, CancellationToken cancellationToken) =>
        await OwnerOfAsync(token, cancellationToken) is not null;

    /// <summary>
    /// Deletes the account. Called only from the confirmation page's POST, never from opening the
    /// link: mail scanners and link previews open the addresses in a message on their own, so a GET
    /// that deletes would do it without a human.
    /// </summary>
    /// <returns><c>false</c> when the token is unknown, expired or already used.</returns>
    public async Task<bool> ConfirmDeletionAsync(string? token, CancellationToken cancellationToken)
    {
        var userId = await OwnerOfAsync(token, cancellationToken);
        if (userId is null)
        {
            metrics.AccountDeletion("invalid-token");
            return false;
        }

        LogDeletingByLink(userId.Value);
        await EraseAsync(userId.Value, cancellationToken);
        metrics.AccountDeletion("deleted");
        return true;
    }

    private async Task<Guid?> OwnerOfAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = SecureTokens.Hash(token);
        var now = time.GetUtcNow();
        return await db.AccountDeletionTokens
            .Where(t => t.TokenHash == tokenHash && t.ExpiresAt > now)
            .Select(t => (Guid?)t.UserId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task EraseAsync(Guid userId, CancellationToken cancellationToken) =>
        transaction.RunAsync(token => eraser.EraseAsync(userId, token), cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting account {UserId} at the user's request (GDPR)")]
    private partial void LogDeletingOnRequest(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting account {UserId}, confirmed with the e-mail link (GDPR)")]
    private partial void LogDeletingByLink(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Issued an account deletion link for account {UserId}")]
    private partial void LogLinkIssued(Guid userId);
}
