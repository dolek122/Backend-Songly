using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Common.Events;
using Songly.Common.Persistence;
using Songly.Common.Throttling;
using Songly.Identity.Contracts;
using Songly.Identity.Domain;
using Songly.Identity.Http;
using Songly.Identity.Persistence;
using Songly.Identity.Security;
using Songly.Identity.Sessions;

namespace Songly.Identity.Registration;

/// <summary>
/// Registration with e-mail verification (original ADR 0009).
/// </summary>
/// <remarks>
/// <para>
/// Registration used to create the account at once and answer 409 with the address for a taken
/// one — an <b>oracle for the existence of accounts</b>, the very thing login and password reset
/// defend against on their own. Now both cases look identical (202) and differ only in <b>the
/// message the mailbox owner receives</b>: a free address gets a confirmation link, a taken one a
/// warning that someone tried to register. The person asking learns nothing unless they own the
/// mailbox — and then they have a right to know.
/// </para>
/// <para>
/// <b>The account does not exist until the address is confirmed.</b> An unconfirmed registration
/// is not a row in <c>users</c>, so no other module ever has to learn that a user can be
/// "unconfirmed" — a flag on <c>users</c> would leak that state into profiles, search and matching.
/// </para>
/// <para>
/// Mail goes out only after the data is saved. A link sent before a failed save would point to a
/// registration that never existed. (The original needed an after-commit event listener for that;
/// with explicit saves it is simply the order of the statements.)
/// </para>
/// </remarks>
internal sealed partial class RegistrationService(
    IdentityDbContext db,
    PasswordHasher passwords,
    TokenIssuer tokens,
    RegistrationMailer mailer,
    IIntervalGate gate,
    IDomainEvents events,
    IOptions<RegistrationOptions> options,
    IdentityMetrics metrics,
    TimeProvider time,
    ILogger<RegistrationService> logger)
{
    private const string ExistingAccountWarningKeyPrefix = "registration:existing-account-warning:";

    /// <summary>
    /// Accepts a registration. Returns nothing, and always the same way.
    /// </summary>
    /// <remarks>
    /// The password is hashed <b>before anything else</b>, also on the "address taken" path where the
    /// hash is never stored — so that both paths cost about the same. bcrypt costs orders of
    /// magnitude more than a lookup by e-mail; without this, the response time alone would rebuild
    /// the oracle the rest of this method removes.
    /// </remarks>
    public async Task RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        metrics.Registration("requested");
        var email = request.Email!;
        var passwordHash = passwords.Hash(request.Password!);
        var settings = options.Value;
        var now = time.GetUtcNow();

        var owner = await db.Users
            .Where(user => user.Email == email)
            .Select(user => new { user.Id, user.Email })
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is not null)
        {
            metrics.Registration("existing-account");
            LogRegistrationOnExistingAccount(owner.Id);
            if (await gate.TryPassAsync(ExistingAccountWarningKeyPrefix + owner.Id, settings.ResendInterval))
            {
                await mailer.SendExistingAccountWarningAsync(owner.Id, owner.Email, cancellationToken);
            }

            return;
        }

        var token = SecureTokens.Generate();
        var pending = await db.PendingRegistrations.SingleOrDefaultAsync(p => p.Email == email, cancellationToken);
        if (pending is null)
        {
            db.PendingRegistrations.Add(new PendingRegistration(email, passwordHash, SecureTokens.Hash(token), now, settings.Ttl));
            metrics.Registration("pending");
        }
        else if (pending.CreatedAt > now - settings.ResendInterval)
        {
            // The same 202 — a distinguishable 429 would tell the asker that a registration for this
            // address was just requested.
            metrics.Registration("throttled");
            return;
        }
        else
        {
            // A new link, the original password — see PendingRegistration.Reissue.
            pending.Reissue(SecureTokens.Hash(token), now, settings.Ttl);
            metrics.Registration("resent");
        }

        await db.SaveChangesAsync(cancellationToken);
        await mailer.SendVerificationLinkAsync(email, token, cancellationToken);
    }

    /// <summary>
    /// Confirms the address — and only now creates the account.
    /// </summary>
    /// <remarks>
    /// Returns a token pair, logging the user in at once. That is not convenience at the expense of
    /// security: clicking the link proves control of the mailbox, and the same person typed the
    /// password a moment ago. Making them log in right after would verify nothing more.
    /// </remarks>
    public async Task<AuthTokensResponse> VerifyAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = SecureTokens.Hash(request.Token!);
        var pending = await db.PendingRegistrations.SingleOrDefaultAsync(p => p.TokenHash == tokenHash, cancellationToken);
        var now = time.GetUtcNow();
        if (pending is null || pending.IsExpiredAt(now))
        {
            throw InvalidToken();
        }

        // One transaction across modules (ADR 0006): the account, the profile other modules create
        // for it, and the session exist together or not at all.
        using var transaction = ModuleTransaction.Begin();

        // A race: someone may have confirmed the same address through another registration in the
        // meantime. Clean up and refuse exactly as for a bad token — a distinct message would tell
        // the asker that the address has an account.
        if (await db.Users.AnyAsync(user => user.Email == pending.Email, cancellationToken))
        {
            db.PendingRegistrations.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);
            transaction.Complete();
            throw InvalidToken();
        }

        var user = new User(pending.Email, pending.PasswordHash, now);
        db.Users.Add(user);
        db.PendingRegistrations.Remove(pending);

        // Saved before the handlers and the session: their rows reference the user, and EF Core
        // does not know that (no navigation is mapped), so it would not order the inserts.
        await db.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(new UserRegistered(user.Id, now), cancellationToken);
        var response = await tokens.IssueForAsync(user.Id, cancellationToken);
        transaction.Complete();

        metrics.Registration("verified");
        LogVerified(user.Id);
        return response;
    }

    private InvalidVerificationTokenException InvalidToken()
    {
        metrics.Registration("invalid-token");
        LogInvalidToken();
        return new InvalidVerificationTokenException();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Registration attempted on the address of existing account {UserId}")]
    private partial void LogRegistrationOnExistingAccount(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail address confirmed, account {UserId} created")]
    private partial void LogVerified(Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected an e-mail verification token")]
    private partial void LogInvalidToken();
}
