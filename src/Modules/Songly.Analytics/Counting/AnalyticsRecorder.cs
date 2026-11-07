using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Songly.Analytics.Domain;
using Songly.Analytics.Persistence;

namespace Songly.Analytics.Counting;

/// <summary>
/// Counts one domain event in two independent places: a durable row for the product analytics
/// (<see cref="SummaryService"/>), and <c>songly.domain.events{type}</c> for observability.
/// </summary>
/// <remarks>
/// The counter goes up after the save — when the write fails, the metric does not count an event the database
/// does not have. A reaction to a committed fact, so a failure is logged and swallowed, like a notification
/// (this repository's ADR 0012).
/// </remarks>
internal sealed partial class AnalyticsRecorder
{
    public const string MeterName = "Songly.Analytics";

    private readonly AnalyticsDbContext _db;
    private readonly ILogger<AnalyticsRecorder> _logger;
    private readonly Counter<long> _domainEvents;

    public AnalyticsRecorder(AnalyticsDbContext db, IMeterFactory meterFactory, ILogger<AnalyticsRecorder> logger)
    {
        _db = db;
        _logger = logger;
        _domainEvents = meterFactory.Create(MeterName).CreateCounter<long>("songly.domain.events");
    }

    public async Task RecordAsync(AnalyticsEventType type, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        try
        {
            _db.Events.Add(new AnalyticsEvent(type, occurredAt));
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _db.ChangeTracker.Clear();
            LogRecordFailed(type, exception);
            return;
        }

        _domainEvents.Add(1, new KeyValuePair<string, object?>("type", JsonNamingPolicy.SnakeCaseUpper.ConvertName(type.ToString())));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not count a {Type} event")]
    private partial void LogRecordFailed(AnalyticsEventType type, Exception exception);
}
