using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Songly.Common.Web;

/// <summary>Writes an <see cref="ApiError"/> as the response body.</summary>
public static class ApiErrorWriter
{
    public static Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        var time = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        var path = context.Request.PathBase.Add(context.Request.Path).Value ?? "/";

        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new ApiError(code, message, time.GetUtcNow(), path));
    }
}
