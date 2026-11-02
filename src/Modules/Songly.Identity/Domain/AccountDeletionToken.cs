namespace Songly.Identity.Domain;

/// <summary>
/// One-time token from the "delete my account" link (original ADR 0043), stored only as its hash —
/// like a password-reset token. No "used" flag: using it deletes the account and its tokens with it.
/// </summary>
internal sealed class AccountDeletionToken
{
    // For EF Core.
    private AccountDeletionToken() => TokenHash = string.Empty;

    public AccountDeletionToken(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = now + lifetime;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
}
