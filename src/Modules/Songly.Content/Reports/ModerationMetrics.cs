using System.Diagnostics.Metrics;

namespace Songly.Content.Reports;

/// <summary>
/// <c>songly.moderation.report.rejected{reason}</c> (original ADR 0019): reports turned away by a
/// gate. Both series stay low in healthy traffic, so the increase matters, not the value —
/// <c>rate-limit</c> is someone flooding the queue, <c>banned</c> a banned user knocking on the
/// channel they just lost. Without the counter, hitting either gate is invisible from outside.
/// </summary>
internal sealed class ModerationMetrics
{
    public const string MeterName = "Songly.Content.Moderation";

    private readonly Counter<long> _rejectedReports;

    public ModerationMetrics(IMeterFactory meterFactory) =>
        _rejectedReports = meterFactory.Create(MeterName).CreateCounter<long>("songly.moderation.report.rejected");

    public void ReportRejected(string reason) => _rejectedReports.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
