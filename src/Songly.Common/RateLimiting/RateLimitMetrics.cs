using System.Diagnostics.Metrics;

namespace Songly.Common.RateLimiting;

/// <summary>
/// Refusals, which are otherwise invisible from the outside. Two metrics, not one with two tags: Prometheus wants
/// the same tag keys within one metric, and one is tagged by <c>path</c>, the other by <c>rule</c>. Both tags come
/// from the configuration — a pattern, never the request's own path, or the tag's values would be unbounded.
/// </summary>
internal sealed class RateLimitMetrics
{
    public const string MeterName = "Songly.RateLimiting";

    private readonly Counter<long> _rejected;
    private readonly Counter<long> _userRejected;

    public RateLimitMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _rejected = meter.CreateCounter<long>("songly.rate.limit.rejected");
        _userRejected = meter.CreateCounter<long>("songly.rate.limit.user.rejected");
    }

    public void Rejected(string pattern) => _rejected.Add(1, new KeyValuePair<string, object?>("path", pattern));

    public void UserRejected(string rule) => _userRejected.Add(1, new KeyValuePair<string, object?>("rule", rule));
}
