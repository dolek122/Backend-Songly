namespace Songly.Identity.Security;

internal static class Policies
{
    /// <summary>
    /// The short, closed list of what a suspended account may still do: learn its own state and
    /// appeal (and, once ported, delete itself — a GDPR right a sanction must not take away).
    /// </summary>
    public const string UserOrSuspended = nameof(UserOrSuspended);
}
