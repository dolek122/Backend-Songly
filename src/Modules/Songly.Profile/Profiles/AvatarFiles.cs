using Microsoft.Extensions.Logging;
using Songly.Common.Media;
using Songly.Common.Storage;
using Songly.Profile.Domain;

namespace Songly.Profile.Profiles;

/// <summary>Avatar files in the object storage: their keys, public addresses and deletion.</summary>
internal sealed partial class AvatarFiles(IObjectStorage storage, ILogger<AvatarFiles> logger)
{
    /// <summary>
    /// A random part in the key, rather than a fixed <c>avatars/{userId}</c>: a CDN caches the old
    /// address, and overwriting the file in place would show the previous photo for hours — a change
    /// that looks ignored. The extension comes from the detected type, never from the upload's name:
    /// otherwise a file could be stored as <c>.html</c>, earning an address that serves someone
    /// else's HTML from our domain.
    /// </summary>
    public static string NewKey(Guid userId, ImageType type) => $"avatars/{userId}/{Guid.NewGuid()}.{type.Extension}";

    public string? UrlOf(UserProfile profile) => profile.AvatarKey is null ? null : storage.PublicUrl(profile.AvatarKey);

    public Task StoreAsync(string key, SanitizedImage image, CancellationToken cancellationToken) =>
        storage.PutAsync(key, image.Content, image.Type.ContentType, cancellationToken);

    /// <summary>
    /// Deletes a file nothing points to any more. Called only once the database has stopped pointing
    /// to it — after the save, or after the commit. A storage failure stays in the log: the file is
    /// orphaned, which is the side of the mistake that destroys nothing (original Claude.md §9).
    /// </summary>
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete avatar {Key}; it stays orphaned")]
    private partial void LogOrphaned(string key, Exception exception);
}
