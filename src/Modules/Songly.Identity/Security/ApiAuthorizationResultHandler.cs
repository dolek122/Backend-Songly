using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Songly.Identity.Security;

/// <summary>
/// Answers failed authorization with the API's error format instead of the framework's empty
/// 401/403.
/// </summary>
internal sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            return AuthorizationFailures.Unauthenticated(context);
        }

        return authorizeResult.Forbidden ? AuthorizationFailures.Forbidden(context) : next(context);
    }
}
