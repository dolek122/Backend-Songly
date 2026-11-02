namespace Songly.Identity.Security;

/// <summary>Settings of the access token (JWT) and the refresh-token session.</summary>
internal sealed class JwtOptions
{
    public const string Section = "Songly:Security:Jwt";

    /// <summary>HMAC-SHA256 signing key. At least 32 bytes — HS256 refuses shorter keys.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// Short on purpose: the access token is stateless, so revoking sessions (password change,
    /// detected token theft) cannot cut it off — only its expiry does.
    /// </summary>
    public TimeSpan AccessTokenTtl { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenTtl { get; set; } = TimeSpan.FromDays(30);
}
