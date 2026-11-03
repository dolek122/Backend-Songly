using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Songly.Common.Media;
using Songly.Common.Storage;
using Songly.Content.Domain;
using Songly.Content.Http;
using Songly.Content.Persistence;
using Songly.Content.Posts;

namespace Songly.Content.Images;

/// <summary>
/// Post images from upload to publication (original ADR 0048).
/// </summary>
/// <remarks>
/// Uploading is a request of its own, before the post (original ADR 0010), so the file exists
/// before anything points to it. Until ADR 0048 nothing counted or cleaned those files: every
/// abandoned image stayed in the storage forever, and the only limit was the rate of writes —
/// hundreds of gigabytes a day from one account. The register of uploads closes both holes.
/// </remarks>
internal sealed class PostImageUploads(
    ContentDbContext db,
    IObjectStorage storage,
    PostImageFiles files,
    IOptions<PostImageOptions> options,
    TimeProvider time)
{
    /// <summary>
    /// Stores the image — stripped of its metadata, GPS included (<see cref="SanitizedImage"/>,
    /// original ADR 0047) — and registers its key, before any post exists.
    /// </summary>
    /// <remarks>
    /// The file goes to the storage before the row, like an avatar: a failed save leaves an orphaned
    /// file, not a row without a file. Above <see cref="PostImageOptions.MaxPending"/> the oldest
    /// pending uploads are evicted — the row first, then the file, and only by whoever actually
    /// deleted the row.
    /// </remarks>
    public async Task<PostImageResponse> UploadAsync(Guid ownerId, byte[] content, CancellationToken cancellationToken)
    {
        var image = SanitizedImage.From(content);
        var key = $"posts/{ownerId}/{Guid.NewGuid()}.{image.Type.Extension}";
        await storage.PutAsync(key, image.Content, image.Type.ContentType, cancellationToken);
        db.PostImageUploads.Add(new PostImageUpload(key, ownerId, time.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);

        var evicted = await db.PostImageUploads
            .Where(u => u.OwnerId == ownerId)
            .OrderByDescending(u => u.CreatedAt).ThenByDescending(u => u.ImageKey)
            .Skip(options.Value.MaxPending)
            .Select(u => u.ImageKey)
            .ToListAsync(cancellationToken);
        foreach (var evictedKey in evicted)
        {
            if (await DeleteRowAsync(evictedKey, cancellationToken))
            {
                await files.DeleteOrphanedAsync(evictedKey, cancellationToken);
            }
        }

        return new PostImageResponse(key, storage.PublicUrl(key));
    }

    /// <summary>
    /// Attaches an image to the post being published by using up its row — inside the publication's
    /// transaction, so that a refusal later in the publication gives the row back with everything else.
    /// </summary>
    /// <remarks>
    /// The register replaces checking the shape of the key: only a key we issued to this user and
    /// that is not attached yet passes. Someone else's, a made-up one, or one with <c>..</c> in it has
    /// no row. The same key cannot go to two posts either — it used to, and deleting one post deleted
    /// the other's image. <b>The number of deleted rows is the answer</b>: when publishing races the
    /// cleanup, both delete the same row, Postgres lets one through, and whoever got 0 must neither
    /// attach the image nor delete the file.
    /// </remarks>
    public async Task<string?> ClaimAsync(Guid ownerId, string? imageKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageKey))
        {
            return null;
        }

        var claimed = await db.PostImageUploads.Where(u => u.ImageKey == imageKey && u.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
        return claimed == 1 ? imageKey : throw new UnknownPostImageException();
    }

    /// <summary>
    /// Removes images that waited for a post longer than <see cref="PostImageOptions.PendingTtl"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The row first, then the file</b>, each row in a statement of its own. Only whoever deleted
    /// the row deletes the file, so an image attached to a post at the same moment stays. The other
    /// order would, in that race, give a post pointing to a file that is gone.
    /// </para>
    /// <para>
    /// A storage failure <b>stops the run</b> instead of going on: the row is already gone, so its file
    /// stays orphaned. During a storage outage going on would orphan every remaining file; stopping
    /// orphans one. That is why this calls the storage directly and lets its exception out.
    /// </para>
    /// </remarks>
    public async Task<int> RemoveAbandonedAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = time.GetUtcNow() - settings.PendingTtl;
        var removed = 0;
        List<string> keys;
        do
        {
            keys = await db.PostImageUploads
                .Where(u => u.CreatedAt < cutoff)
                .OrderBy(u => u.CreatedAt).ThenBy(u => u.ImageKey)
                .Select(u => u.ImageKey)
                .Take(settings.CleanupBatchSize)
                .ToListAsync(cancellationToken);
            foreach (var key in keys)
            {
                if (await DeleteRowAsync(key, cancellationToken))
                {
                    await storage.DeleteAsync(key, cancellationToken);
                    removed++;
                }
            }
        }
        while (keys.Count == settings.CleanupBatchSize);

        return removed;
    }

    private async Task<bool> DeleteRowAsync(string key, CancellationToken cancellationToken) =>
        await db.PostImageUploads.Where(u => u.ImageKey == key).ExecuteDeleteAsync(cancellationToken) == 1;
}
