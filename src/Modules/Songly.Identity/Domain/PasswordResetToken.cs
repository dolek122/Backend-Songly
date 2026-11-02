namespace Songly.Identity.Domain;

/// <summary>
/// A one-time password-reset token, stored only as its SHA-256 — like the refresh token of a
/// <see cref="Session"/>.
/// </summary>
/// <remarks>
/// No "used" flag on purpose: a successful reset deletes every token of the account, so clicking
/// the same link again finds no row and gets the answer of a fabricated token. A <c>used_at</c>
/// column would behave the same but leave rows behind for a cleanup job.
/// </remarks>
internal sealed class PasswordResetToken
{
    // For EF Core.
    private PasswordResetToken() => TokenHash = string.Empty;

    public PasswordResetToken(Guid userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
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
