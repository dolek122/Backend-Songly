using Microsoft.Extensions.Logging;
using Songly.ModerationLog.Domain;
using Songly.ModerationLog.Persistence;

namespace Songly.ModerationLog.Recording;

/// <summary>
/// Appends entries. There is no method that changes or removes one — apart from the deletion of an account or a
/// community, which is not a correction of the history but the end of what it describes.
/// </summary>
/// <remarks>
/// Called after the save that made the decision true (the order of statements is the "after commit", ADR 0005):
/// an entry about a ban whose transaction rolled back would be the history of something that never happened,
/// and an append-only history cannot be corrected later. A reaction to a committed fact, so a failure is
/// logged and swallowed — the decision stands without its entry (ADR 0012).
/// </remarks>
internal sealed partial class ModerationLogRecorder(ModerationLogDbContext db, ILogger<ModerationLogRecorder> logger)
{
    /// <summary>All entries of one decision in one save: all of them or none.</summary>
    public async Task RecordAllAsync(IReadOnlyCollection<ModerationLogEntry> entries, CancellationToken cancellationToken)
    {
        try
        {
            db.Entries.AddRange(entries);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            foreach (var entry in entries)
            {
                LogRecordFailed(entry.Action, entry.SubjectId, exception);
            }
        }
    }

    public Task RecordAsync(ModerationLogEntry entry, CancellationToken cancellationToken) =>
        RecordAllAsync([entry], cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record {Action} about {SubjectId} in the moderation log")]
    private partial void LogRecordFailed(ModerationAction action, Guid subjectId, Exception exception);
}
