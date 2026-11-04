using Microsoft.Extensions.Logging;
using Songly.Common.Storage;

namespace Songly.Content.Images;

/// <summary>Deleting the files of post images nothing points to any more.</summary>
/// <remarks>
/// One place for every way an image is orphaned: the author deletes the post, a moderator deletes
/// it, the community is deleted, the account is deleted — and a pending upload is evicted. Called
/// only once the database has stopped pointing to the file: after the save, or after the commit
/// (ADR 0010). Rows go inside the transaction, files after it, because a file deleted too early
/// leaves, after a rollback, a post pointing at an image that can never be shown or recovered; the
/// opposite mistake costs an orphaned file.
/// </remarks>
internal sealed partial class PostImageFiles(IObjectStorage storage, ILogger<PostImageFiles> logger)
{
    /// <summary>There is nothing left to roll back, so a storage failure stays in the log and the file stays orphaned.</summary>
    public async Task DeleteOrphanedAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(key, cancellationToken);
        }
        catch (ObjectStorageException exception)
        {
            LogOrphaned(key, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete post image {Key}; it stays orphaned")]
    private partial void LogOrphaned(string key, Exception exception);
}
