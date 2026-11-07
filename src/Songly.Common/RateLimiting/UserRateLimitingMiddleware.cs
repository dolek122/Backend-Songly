using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Common.Web;

namespace Songly.Common.RateLimiting;

/// <summary>
/// A limit on writes per user on the paths behind a token (original ADR 0034) — the other half of
/// <see cref="ClientRateLimitingMiddleware"/>. It runs after authentication, so it knows who is asking and keys by
/// the account's id; it knows nothing else about the domain. Requests without an account pass uncounted: the
/// public paths have the limit per address, and the rest is refused by authorization anyway.
/// </summary>
internal sealed partial class UserRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly FixedWindowCounter _counter;
    private readonly RateLimitMetrics _metrics;
    private readonly bool _enabled;
    private readonly IReadOnlyList<CompiledRule> _rules;
    private readonly ILogger<UserRateLimitingMiddleware> _logger;

    public UserRateLimitingMiddleware(
        RequestDelegate next,
        FixedWindowCounter counter,
        RateLimitMetrics metrics,
        IOptions<UserRateLimitOptions> options,
        ILogger<UserRateLimitingMiddleware> logger)
    {
        _next = next;
        _counter = counter;
        _metrics = metrics;
        _enabled = options.Value.Enabled;
        _rules = [.. (options.Value.Rules.Count > 0 ? options.Value.Rules : UserRateLimitOptions.DefaultRules).Select(CompiledRule.From)];
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || !context.User.TryGetUserId(out var userId))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        string? rejectedBy = null;
        long retryAfter = 0;
        foreach (var rule in _rules.Where(rule => rule.Matches(context.Request.Method, path)))
        {
            // The window is keyed by the rule, not the path: the whole group shares one budget, or spreading the
            // flood over several endpoints would multiply the limit.
            var key = $"rate-limit:user:{rule.Name}:{userId}";
            var count = await _counter.IncrementAsync(key, rule.Window);
            if (count > rule.Limit)
            {
                rejectedBy ??= rule.Name;

                // With several rules exceeded, the latest window is the one to wait for — a shorter Retry-After
                // would promise a return that ends in another 429.
                retryAfter = Math.Max(retryAfter, await _counter.RetryAfterSecondsAsync(key, rule.Window));
            }
        }

        if (rejectedBy is not null)
        {
            // The user's id is on the log line already (the request's logging scope); here only the rule.
            _metrics.UserRejected(rejectedBy);
            LogRejected(context.Request.Method, rejectedBy);
            await TooManyRequests.WriteAsync(context, retryAfter);
            return;
        }

        await _next(context);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "User rate limit exceeded on a {Method} (rule {Rule})")]
    private partial void LogRejected(string method, string rule);

    private sealed record CompiledRule(string Name, IReadOnlySet<string> Methods, IReadOnlyList<PathPattern> Paths, int Limit, TimeSpan Window)
    {
        public static CompiledRule From(UserRateLimitRule rule) => new(
            rule.Name,
            (rule.Methods.Count > 0 ? rule.Methods : UserRateLimitOptions.WriteMethods).Select(method => method.ToUpperInvariant()).ToHashSet(),
            [.. rule.Paths.Select(path => new PathPattern(path))],
            rule.Limit,
            rule.Window);

        public bool Matches(string method, string path) =>
            Methods.Contains(method) && Paths.Any(pattern => pattern.Matches(path));
    }
}
