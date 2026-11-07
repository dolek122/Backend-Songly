namespace Songly.Common.RateLimiting;

/// <summary>The limit per client address on the public paths (<c>Songly:Security:RateLimit</c>).</summary>
internal sealed class RateLimitOptions
{
    public const string Section = "Songly:Security:RateLimit";

    /// <summary>
    /// Every path exposed anonymously: the two ways in (registration, login), the two working on a refresh token,
    /// the two of the password reset, the public OAuth callback and the account deletion page without the app
    /// (original ADR 0043 — its form sends mail, so it needs the same brake as the reset). Paths behind a token
    /// are limited per user instead (<see cref="UserRateLimitOptions"/>): a counter per address does not know
    /// who is asking.
    /// </summary>
    /// <remarks>
    /// For the password reset the limit per address is only half of the protection — it does not stop flooding
    /// someone's mailbox from many addresses, the victim is one. The other half is the interval per account
    /// (<c>Songly:Security:PasswordReset:ResendInterval</c>).
    /// </remarks>
    public static readonly IReadOnlyList<string> DefaultProtectedPaths =
    [
        "/api/v1/auth/register",
        "/api/v1/auth/register/verify",
        "/api/v1/auth/login",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",
        "/api/v1/auth/password/reset-request",
        "/api/v1/auth/password/reset",
        "/api/v1/now-playing/spotify/callback",
        "/account-deletion",
        "/account-deletion/confirm",
    ];

    public bool Enabled { get; set; } = true;

    public int Limit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Empty means <see cref="DefaultProtectedPaths"/>. Setting it replaces the whole list — the defaults are
    /// filled in only when nothing is configured, because the configuration binder appends to a list it finds.
    /// </summary>
    public List<string> ProtectedPaths { get; set; } = [];
}
