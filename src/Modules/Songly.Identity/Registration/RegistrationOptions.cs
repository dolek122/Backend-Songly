namespace Songly.Identity.Registration;

internal sealed class RegistrationOptions
{
    public const string Section = "Songly:Security:Registration";

    /// <summary>
    /// Longer than a password-reset link (30 minutes), on purpose. A reset is done in reaction to a
    /// problem, with the mailbox open; a registration is often started "for later". A day keeps
    /// the link from lying in someone's inbox for weeks, and it opens nothing that already exists.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Minimum time between two messages to the same address — the verification link and the
    /// "someone tried to register" warning alike. Rate limiting counts per IP address, so on its
    /// own it does not stop flooding one inbox from many addresses.
    /// </summary>
    public TimeSpan ResendInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Link template with a <c>{token}</c> placeholder. It leads to the mobile app (original
    /// ADR 0037) — the backend has no UI, so a link straight to the REST endpoint would show raw JSON.
    /// </summary>
    public string VerificationUrl { get; set; } = "songly://verify-email?token={token}";

    public bool CleanupEnabled { get; set; } = true;

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
