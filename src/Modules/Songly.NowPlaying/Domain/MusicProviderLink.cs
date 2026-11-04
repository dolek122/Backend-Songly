namespace Songly.NowPlaying.Domain;

/// <summary>
/// A user's account linked with an external music service. The OAuth tokens are kept only
/// encrypted (AES-GCM, original Claude.md §9): they are behavioural data and a key to someone's
/// account elsewhere.
/// </summary>
internal sealed class MusicProviderLink
{
    // For EF Core.
    private MusicProviderLink()
    {
        Provider = string.Empty;
        AccessTokenEncrypted = string.Empty;
        RefreshTokenEncrypted = string.Empty;
    }

    public MusicProviderLink(Guid userId, string provider, string accessTokenEncrypted, string refreshTokenEncrypted, DateTimeOffset accessTokenExpiresAt, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Provider = provider;
        AccessTokenEncrypted = accessTokenEncrypted;
        RefreshTokenEncrypted = refreshTokenEncrypted;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Provider { get; private set; }

    public string AccessTokenEncrypted { get; private set; }

    public string RefreshTokenEncrypted { get; private set; }

    public DateTimeOffset AccessTokenExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsAccessTokenExpired(DateTimeOffset now) => now >= AccessTokenExpiresAt;

    public void UpdateTokens(string accessTokenEncrypted, string refreshTokenEncrypted, DateTimeOffset accessTokenExpiresAt, DateTimeOffset now)
    {
        AccessTokenEncrypted = accessTokenEncrypted;
        RefreshTokenEncrypted = refreshTokenEncrypted;
        AccessTokenExpiresAt = accessTokenExpiresAt;
        UpdatedAt = now;
    }
}
