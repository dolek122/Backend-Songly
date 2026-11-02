using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Songly.Common.Web;

/// <summary>
/// Gives every request an identifier, puts it into the logging scope (so it lands on every log
/// line as <c>requestId</c>) and returns it in the <c>X-Request-Id</c> response header.
/// </summary>
/// <remarks>
/// <para>
/// Without it structured logging is just nicer-looking noise: with a few requests in flight there
/// is no way to tell which lines belong together. A user reporting a bug quotes the header value,
/// and that is the key to exactly their request.
/// </para>
/// <para>
/// This is the first middleware in the pipeline, so rate-limiter rejections and failed
/// authentications get an identifier too — those are the requests investigated most often.
/// The header is written in <c>OnStarting</c>, not up front, because the exception handler clears
/// the response headers before writing a 500 — and a 500 is precisely the response a user reports.
/// </para>
/// </remarks>
internal sealed partial class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";
    public const string LogProperty = "requestId";

    private const int MaxIncomingLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Resolve(context.Request.Headers[HeaderName].ToString());
        context.TraceIdentifier = requestId;
        RequestCorrelation.Current = requestId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { [LogProperty] = requestId }))
        {
            await next(context);
        }
    }

    /// <summary>
    /// An incoming identifier is honoured, so logs can be joined with the mobile client or a proxy,
    /// but it is sanitised and truncated: it is client-controlled and ends up on every log line.
    /// Without the character filter and the length limit anyone could inject arbitrary content,
    /// including fake log entries.
    /// </summary>
    internal static string Resolve(string? incoming)
    {
        var sanitized = DisallowedCharacters().Replace(incoming?.Trim() ?? string.Empty, string.Empty);
        if (sanitized.Length == 0)
        {
            return Guid.NewGuid().ToString();
        }

        return sanitized.Length > MaxIncomingLength ? sanitized[..MaxIncomingLength] : sanitized;
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex DisallowedCharacters();
}
