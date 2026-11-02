using System.Text;
using Microsoft.AspNetCore.Http;

namespace Songly.Identity.AccountDeletion;

/// <summary>An HTML page with the headers every account deletion page needs.</summary>
internal sealed class HtmlPageResult(int statusCode, string html) : IResult
{
    /// <summary>
    /// The page loads nothing from outside itself, so the policy forbids almost everything.
    /// <c>form-action 'self'</c> is the key part: an injected form cannot send the token anywhere else.
    /// </summary>
    private const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";

    public Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;

        // The confirmation page's address carries the token, and its "back" link leads on — the
        // browser must not hand that address to the next page.
        response.Headers["Referrer-Policy"] = "no-referrer";
        return response.WriteAsync(html, Encoding.UTF8);
    }
}
