namespace Songly.Identity.Contracts;

/// <summary>
/// Named authorization policies for endpoints of other modules. Everything under <c>/api/v1/admin</c> is
/// admin-only by its path already; this is for the admin endpoints the original placed elsewhere.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>The platform's administrators (original ADR 0006): the role read live from the database on every request.</summary>
    public const string Admin = nameof(Admin);
}
