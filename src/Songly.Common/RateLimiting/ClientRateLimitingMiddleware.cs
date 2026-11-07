using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Songly.Common.RateLimiting;

/// <summary>
/// A fixed-window limit per client address on the public paths — against bots creating accounts and against
/// brute force (original Claude.md §9). It runs before authentication, which is why it counts per address: at
/// this point nobody knows yet who is asking. Paths behind a token have <see cref="UserRateLimitingMiddleware"/>.
/// </summary>
internal sealed partial class ClientRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly FixedWindowCounter _counter;
    private readonly RateLimitMetrics _metrics;
    private readonly RateLimitOptions _options;
    private readonly IReadOnlyList<PathPattern> _patterns;
    private readonly ILogger<ClientRateLimitingMiddleware> _logger;

    public ClientRateLimitingMiddleware(
        RequestDelegate next,
        FixedWindowCounter counter,
        RateLimitMetrics metrics,
        IOptions<RateLimitOptions> options,
        ILogger<ClientRateLimitingMiddleware> logger)
    {
        _next = next;
        _counter = counter;
        _metrics = metrics;
        _options = options.Value;
        _patterns = [.. (_options.ProtectedPaths.Count > 0 ? _options.ProtectedPaths : RateLimitOptions.DefaultProtectedPaths).Select(path => new PathPattern(path))];
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var pattern = _options.Enabled ? Matching(context.Request.Path) : null;
        if (pattern is null)
        {
            await _next(context);
            return;
        }

        // The window is keyed by the pattern, not the path: otherwise a pattern with a wildcard would give every
        // path its own window and limit nothing.
        var key = $"rate-limit:{pattern.Pattern}:{ClientAddress(context)}";
        var count = await _counter.IncrementAsync(key, _options.Window);
        if (count > _options.Limit)
        {
            // Someone is hammering a public path; without a log line and a counter, hitting the limit was
            // invisible from the outside.
            _metrics.Rejected(pattern.Pattern);
            LogRejected(pattern.Pattern);
            await TooManyRequests.WriteAsync(context, await _counter.RetryAfterSecondsAsync(key, _options.Window));
            return;
        }

        await _next(context);
    }

    private PathPattern? Matching(PathString path) =>
        _patterns.FirstOrDefault(pattern => pattern.Matches(path.Value ?? "/"));

    /// <summary>
    /// The connection's address, and nothing else. Behind a proxy the real one is put there by the forwarded
    /// headers middleware, which trusts only the entry the proxy itself appended (see <c>UseSonglyRateLimiting</c>).
    /// Reading <c>X-Forwarded-For</c> here would be a hole: without a trusted proxy in front it is just a field
    /// from the client — a free way around the limit by sending a random address every time.
    /// </summary>
    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded on {Pattern}")]
    private partial void LogRejected(string pattern);
}
