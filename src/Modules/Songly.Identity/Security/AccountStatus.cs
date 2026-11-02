namespace Songly.Identity.Security;

/// <summary>The state of an account that authorization needs: administrator, suspended.</summary>
internal sealed record AccountStatus(bool IsAdmin, bool IsSuspended)
{
    /// <summary>
    /// A valid token of an account that no longer exists (deleted after the token was issued). It
    /// reads as an ordinary account: the data it could touch is gone with the account, and an
    /// exception here would turn a stale token into a 500.
    /// </summary>
    public static readonly AccountStatus Unknown = new(IsAdmin: false, IsSuspended: false);
}
