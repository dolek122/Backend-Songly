namespace Songly.Identity.Domain;

/// <summary>
/// A registration waiting for its e-mail address to be confirmed. The account is created only at
/// verification — until then no module outside identity knows the registration exists.
/// </summary>
/// <remarks>
/// The password is already bcrypt-hashed here, just as in <see cref="User"/>. A pending
/// registration must not be a place where the password is protected worse than in the account:
/// the user typed it once and has no idea it lives somewhere else for a while.
/// </remarks>
internal sealed class PendingRegistration
{
    // For EF Core.
    private PendingRegistration()
    {
        Email = string.Empty;
        PasswordHash = string.Empty;
        TokenHash = string.Empty;
    }

    public PendingRegistration(string email, string passwordHash, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        Id = Guid.NewGuid();
        Email = email;
        PasswordHash = passwordHash;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = now + lifetime;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public string TokenHash { get; private set; }

    /// <summary>When the current link was issued — the resend interval counts from here.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>
    /// A repeated registration for the same address issues a new link and invalidates the old one.
    /// </summary>
    /// <remarks>
    /// The password is deliberately <b>not</b> replaced. If it were, a stranger could inject their
    /// own password into someone else's unconfirmed registration and wait for the mailbox owner to
    /// click the newest link — the account would be created with the attacker's password. Whoever
    /// mistyped the first time confirms the address and changes the password the normal way.
    /// </remarks>
    public void Reissue(string newTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        TokenHash = newTokenHash;
        CreatedAt = now;
        ExpiresAt = now + lifetime;
    }
}
