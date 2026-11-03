using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Songly.Common.Web;

/// <summary>
/// The one place that turns exceptions into <see cref="ApiError"/> responses. Codes and messages
/// match the original backend one to one, because the mobile app shows and branches on them.
/// </summary>
/// <remarks>
/// Only the final fallback is allowed to answer 500, and it is the only branch that logs a stack
/// trace. A 500 without a log entry cannot be diagnosed; a 400 with a stack trace floods the logs
/// with something that is not a server failure. The request body is never logged, because it can
/// carry a password or a token.
/// </remarks>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, message) = exception switch
        {
            ApiException api => (api.StatusCode, api.Code, api.Message),
            BadHttpRequestException badRequest => MapBadRequest(badRequest),
            DbUpdateConcurrencyException => Conflict(context, exception),
            DbUpdateException { InnerException: PostgresException { SqlState: ['2', '3', ..] } } => Conflict(context, exception),
            _ => Unexpected(context, exception),
        };

        await ApiErrorWriter.WriteAsync(context, status, code, message);
        return true;
    }

    /// <summary>
    /// A write rejected by a database constraint (SQLSTATE class 23: unique, foreign key, check),
    /// or an update/delete that found its row already gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 409, not 500: the request collides with the current state of the data, the server works.
    /// Usually it is a race — a double tap on "like" sends two requests, both see "not liked yet",
    /// and the second INSERT hits the primary key; or two clicks on the same verification link.
    /// Answering 500 made every such race look like an outage and told the client "try later"
    /// instead of "refresh".
    /// </para>
    /// <para>
    /// The stack trace stays in the log as a warning, because the same symptom comes from a module
    /// that forgot to delete its rows when an account is deleted — that must stay visible. The
    /// message says nothing specific: constraint names describe the schema.
    /// </para>
    /// </remarks>
    private (int, string, string) Conflict(HttpContext context, Exception exception)
    {
        logger.LogWarning(exception, "Write rejected by a database constraint: {Method} {Path}", context.Request.Method, context.Request.Path);
        return (StatusCodes.Status409Conflict, "CONFLICT", "Żądanie koliduje z bieżącym stanem danych — odśwież i spróbuj ponownie");
    }

    private (int, string, string) Unexpected(HttpContext context, Exception exception)
    {
        logger.LogError(exception, "Unhandled exception: {Method} {Path}", context.Request.Method, context.Request.Path);
        return (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Nieoczekiwany błąd serwera");
    }

    /// <summary>
    /// Framework-level request errors. Minimal APIs report all of them as
    /// <see cref="BadHttpRequestException"/>, so the kind is recovered from the status code, the
    /// inner exception and — for binding errors — the framework's message. The message format is
    /// covered by integration tests, so a framework upgrade that changes it fails the build
    /// instead of silently degrading the error codes.
    /// </summary>
    /// <remarks>
    /// Never echo the framework's own message: it names CLR types and fragments of the payload,
    /// i.e. implementation details handed to a public client.
    /// </remarks>
    private static (int, string, string) MapBadRequest(BadHttpRequestException exception)
    {
        // Either the whole request is over its limit (Kestrel answers 413), or one file in a
        // multipart form is over the form's limit — which the form reader reports as a 400 around
        // an InvalidDataException. To the client both are the same: the file is too large.
        if (exception.StatusCode == StatusCodes.Status413PayloadTooLarge
            || exception.InnerException is InvalidDataException { Message: var formError } && formError.StartsWith("Multipart body length limit", StringComparison.Ordinal))
        {
            return (StatusCodes.Status413PayloadTooLarge, "FILE_TOO_LARGE", "Przesłany plik jest za duży");
        }

        if (exception.StatusCode == StatusCodes.Status415UnsupportedMediaType)
        {
            return (StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE", "Nieobsługiwany format treści żądania");
        }

        const int badRequest = StatusCodes.Status400BadRequest;
        const string unreadableBody = "Treść żądania jest pusta lub nie jest poprawnym JSON-em";

        if (exception.InnerException is JsonException || ImplicitBodyMissing().IsMatch(exception.Message))
        {
            return (badRequest, "MALFORMED_REQUEST", unreadableBody);
        }

        if (MissingParameter().Match(exception.Message) is { Success: true } missing)
        {
            return missing.Groups["source"].Value switch
            {
                "body" => (badRequest, "MALFORMED_REQUEST", unreadableBody),

                // A file part of a multipart form (Spring's MissingServletRequestPartException).
                "form" => (badRequest, "MISSING_PARAMETER", $"Brak wymaganej części żądania: {missing.Groups["name"].Value}"),
                _ => (badRequest, "MISSING_PARAMETER", $"Brak wymaganego parametru: {missing.Groups["name"].Value}"),
            };
        }

        if (InvalidParameter().Match(exception.Message) is { Success: true } invalid)
        {
            return (badRequest, "INVALID_PARAMETER", $"Nieprawidłowa wartość parametru: {invalid.Groups["name"].Value}");
        }

        return (badRequest, "MALFORMED_REQUEST", "Żądanie jest niekompletne");
    }

    // Implicit body inferred for parameter "ProbeRequest request" but no body was provided.
    [GeneratedRegex("""^Implicit body inferred for parameter "[^"]+" but no body was provided""")]
    private static partial Regex ImplicitBodyMissing();

    // Required parameter "string name" was not provided from query string.
    [GeneratedRegex("""^Required parameter "\S+ (?<name>[^"]+)" was not provided from (?<source>\w+)""")]
    private static partial Regex MissingParameter();

    // Failed to bind parameter "Guid postId" from "abc".
    [GeneratedRegex("""^Failed to bind parameter "\S+ (?<name>[^"]+)" from""")]
    private static partial Regex InvalidParameter();
}
