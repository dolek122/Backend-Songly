namespace Songly.Identity.PasswordReset;

internal sealed class PasswordResetOptions
{
    public const string Section = "Songly:Security:PasswordReset";

    /// <summary>
    /// Far shorter than a refresh token (30 days), because the link lies in a mailbox — a system we
    /// have no control over. Half an hour is enough to read the e-mail and does not leave a key to
    /// the account in someone's inbox for weeks.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Minimum time between two messages for the <b>same account</b>. Rate limiting counts per IP
    /// address, so without this the endpoint would be a free mail generator aimed at someone
    /// else's inbox: spread the requests over a few addresses and flood the victim.
    /// </summary>
    public TimeSpan ResendInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Link template with a <c>{token}</c> placeholder. It leads to the mobile app, which shows the
    /// new-password form and calls the API (original ADR 0037).
    /// </summary>
    public string ResetUrl { get; set; } = "songly://reset-password?token={token}";
}
