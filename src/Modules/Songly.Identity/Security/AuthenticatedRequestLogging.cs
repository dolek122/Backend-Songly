using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Songly.Common.Web;

namespace Songly.Identity.Security;

/// <summary>
/// Adds the authenticated account's <c>userId</c> to every log line of the request, and leaves an
/// audit entry for every request made with administrator rights.
/// </summary>
/// <remarks>
/// The request id comes first (<c>RequestIdMiddleware</c>); the user is only known after
/// authentication, so it is added here, in a nested scope that ends with the request.
/// Administrators are few and their requests reach data of the whole service, so a trail of each
/// one is cheaper than reconstructing it after the fact.
/// </remarks>
internal sealed partial class AuthenticatedRequestLogging(RequestDelegate next, ILogger<AuthenticatedRequestLogging> logger)
{
    public const string LogProperty = "userId";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.User.TryGetUserId(out var userId))
        {
            await next(context);
            return;
        }

        using (logger.BeginScope(new Dictionary<string, object> { [LogProperty] = userId }))
        {
            if (context.User.IsInRole(Roles.Admin))
            {
                LogAdminRequest(context.Request.Method, context.Request.Path);
            }

            await next(context);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Request with administrator rights: {Method} {Path}")]
    private partial void LogAdminRequest(string method, PathString path);
}
