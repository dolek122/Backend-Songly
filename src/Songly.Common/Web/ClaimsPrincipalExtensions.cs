using System.Security.Claims;

namespace Songly.Common.Web;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated account's id — the <c>sub</c> claim of the access token. Only valid on
    /// endpoints that require authentication; anywhere else it is a programming error.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        principal.TryGetUserId(out var userId)
            ? userId
            : throw new InvalidOperationException("The request is not authenticated");

    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out userId);
}
