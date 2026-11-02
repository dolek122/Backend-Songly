using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Songly.Common.Web;

public static class WebExtensions
{
    /// <summary>JSON shape, error handling and request binding shared by every module.</summary>
    public static IServiceCollection AddSonglyWeb(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.Configure<JsonOptions>(options =>
        {
            // Enum values travel as UPPER_SNAKE_CASE names (MEMBER, COMMUNITY_MODERATOR_GRANTED),
            // exactly like the Java enums of the original API.
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
            options.SerializerOptions.Converters.Add(new UtcInstantJsonConverter());
        });

        // Binding failures (malformed JSON, a missing parameter, "abc" where a UUID belongs) would
        // otherwise end as an empty 400. Throwing routes them through GlobalExceptionHandler, so
        // they get the same error format as everything else.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        return services;
    }

    /// <summary>
    /// The outermost part of the pipeline: request id first, then error handling. Must run before
    /// anything that can reject a request, so rejections are correlated and well-formed too.
    /// </summary>
    public static IApplicationBuilder UseSonglyWeb(this IApplicationBuilder app)
    {
        app.UseMiddleware<RequestIdMiddleware>();
        // The empty fallback pipeline is never reached — GlobalExceptionHandler handles every
        // exception — but without it the middleware refuses to start unless ProblemDetails is
        // registered, and ProblemDetails is not this API's error format.
        app.UseExceptionHandler(_ => { });
        app.UseStatusCodePages(context => WriteEmptyStatusAsError(context.HttpContext));
        return app;
    }

    /// <summary>
    /// Responses the framework produces without a body — no route matched, the route exists but not
    /// for this method, or not for this content type (routing answers 415 itself, before binding).
    /// Without this they would be the only errors outside the common format, and a typo in a client
    /// URL would look like something other than "not found".
    /// </summary>
    private static Task WriteEmptyStatusAsError(HttpContext context)
    {
        var request = context.Request;
        return context.Response.StatusCode switch
        {
            StatusCodes.Status404NotFound => ApiErrorWriter.WriteAsync(
                context, StatusCodes.Status404NotFound, "NOT_FOUND", $"Nie znaleziono zasobu: {request.PathBase.Add(request.Path)}"),
            StatusCodes.Status405MethodNotAllowed => ApiErrorWriter.WriteAsync(
                context, StatusCodes.Status405MethodNotAllowed, "METHOD_NOT_ALLOWED", $"Metoda {request.Method} nie jest obsługiwana dla tego zasobu"),
            StatusCodes.Status415UnsupportedMediaType => ApiErrorWriter.WriteAsync(
                context, StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE", "Nieobsługiwany format treści żądania"),
            _ => Task.CompletedTask,
        };
    }
}
