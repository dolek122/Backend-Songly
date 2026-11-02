namespace Songly.Identity.AccountDeletion;

internal sealed class AccountDeletionOptions
{
    public const string Section = "Songly:Security:AccountDeletion";

    /// <summary>
    /// An hour, not half like a reset: the link leads to an irreversible decision, and someone may
    /// want to read the page to the end before clicking. Anyone who comes back a week later asks again.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The same protection of someone's inbox as for password reset: the page is public.</summary>
    public TimeSpan ResendInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Link template with <c>{token}</c>. Unlike the other links this is the backend's own <b>HTTP</b>
    /// address, not <c>songly://</c>: the whole path exists for someone who no longer has the app.
    /// </summary>
    /// <remarks>
    /// From configuration, never from the request's <c>Host</c> header. An address built from the
    /// request could be redirected with a forged header, and the link in the victim's mailbox would
    /// lead to the attacker's server — with the token that deletes the victim's account.
    /// </remarks>
    public string ConfirmUrl { get; set; } = "http://localhost:8080/account-deletion/confirm?token={token}";
}
