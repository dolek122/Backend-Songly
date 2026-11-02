using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Security;

/// <summary>401 and 403 in the API's error format.</summary>
internal static class AuthorizationFailures
{
    public static Task Unauthenticated(HttpContext context) =>
        ApiErrorWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "Wymagane uwierzytelnienie");

    /// <summary>
    /// A suspended account gets its <b>own code</b>, not the generic <c>FORBIDDEN</c> (original
    /// ADR 0022). Otherwise the client cannot tell "you may not access this" from "your account
    /// is suspended", and the user sees an app in which everything randomly stopped working — a
    /// decision that looks like an outage.
    /// </summary>
    public static Task Forbidden(HttpContext context) => context.User.IsInRole(Roles.Suspended)
        ? ApiErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden, "ACCOUNT_SUSPENDED", "Konto jest zawieszone — szczegóły pod GET /api/v1/auth/me/suspension")
        : ApiErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden, "FORBIDDEN", "Brak uprawnień do tego zasobu");
}
