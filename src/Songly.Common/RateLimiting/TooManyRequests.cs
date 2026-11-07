using System.Globalization;
using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Common.RateLimiting;

/// <summary>The 429 both limits answer with, identical in both, in the common error format.</summary>
internal static class TooManyRequests
{
    public const string Code = "RATE_LIMIT_EXCEEDED";

    /// <summary>
    /// With <c>Retry-After</c>: without it the client does not know when it may try again, so it keeps
    /// hammering — exactly what the limit is there to stop.
    /// </summary>
    public static Task WriteAsync(HttpContext context, long retryAfterSeconds)
    {
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return ApiErrorWriter.WriteAsync(context, StatusCodes.Status429TooManyRequests, Code, "Zbyt wiele żądań, spróbuj ponownie później");
    }
}
