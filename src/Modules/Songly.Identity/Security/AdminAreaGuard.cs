using Microsoft.AspNetCore.Http;

namespace Songly.Identity.Security;

/// <summary>
/// Everything under <c>/api/v1/admin</c> requires the administrator role — decided by the path,
/// not by each endpoint (original ADR 0021).
/// </summary>
/// <remarks>
/// A rule per endpoint would protect an admin endpoint only once somebody remembers to add it. By
/// path, the next admin endpoint — in whichever module — is protected the moment it exists, and
/// not even an <c>AllowAnonymous</c> on it can open it. Unknown paths under the prefix are
/// answered the same way, so the guard does not reveal which admin endpoints exist.
/// </remarks>
internal sealed class AdminAreaGuard(RequestDelegate next)
{
    private static readonly PathString AdminArea = new("/api/v1/admin");

    public Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(AdminArea))
        {
            return next(context);
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return AuthorizationFailures.Unauthenticated(context);
        }

        return context.User.IsInRole(Roles.Admin) ? next(context) : AuthorizationFailures.Forbidden(context);
    }
}
