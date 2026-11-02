namespace Songly.Identity.Domain;

/// <summary>
/// A refresh-token session. It is what makes revocation possible without waiting for a token to
/// expire: the access token is stateless (a JWT), the refresh token is not.
/// </summary>
/// <remarks>
/// Only the SHA-256 of the refresh token is stored. A leaked database must not be a list of
/// working sessions.
/// </remarks>
internal sealed class Session
{
    // For EF Core.
    private Session() => RefreshTokenHash = string.Empty;

    private Session(Guid id, Guid userId, string refreshTokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        Id = id;
        UserId = userId;
        RefreshTokenHash = refreshTokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string RefreshTokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public bool Revoked { get; private set; }

    /// <summary><c>null</c> until revoked.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public static Session Start(Guid userId, string refreshTokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new(Guid.NewGuid(), userId, refreshTokenHash, now, now + lifetime);

    public bool IsActiveAt(DateTimeOffset now) => !Revoked && now < ExpiresAt;

    public void Revoke(DateTimeOffset now)
    {
        Revoked = true;
        RevokedAt = now;
    }
}
