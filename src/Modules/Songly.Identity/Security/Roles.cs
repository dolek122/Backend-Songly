namespace Songly.Identity.Security;

/// <summary>
/// Roles of the current request, computed from the account's live state on every request
/// (original ADR 0006, 0022) — never stored in the token.
/// </summary>
internal static class Roles
{
    /// <summary>Full rights of an account. Every endpoint requires it unless it says otherwise.</summary>
    public const string User = "USER";

    /// <summary>
    /// A suspended account gets this <b>instead of</b> <see cref="User"/>, not next to it. The
    /// roles are the whole enforcement of a suspension: every endpoint requires
    /// <see cref="User"/> by default, so a new endpoint is closed to suspended accounts without
    /// anyone having to remember a check.
    /// </summary>
    public const string Suspended = "SUSPENDED";

    public const string Admin = "ADMIN";
}
