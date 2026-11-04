namespace Songly.Content.Domain;

/// <summary>
/// An image uploaded but not attached to a post yet (original ADR 0048).
/// </summary>
/// <remarks>
/// The row lives exactly as long as the file waits for a post: publishing the post uses it up, the
/// cleanup removes abandoned ones. A key attached to a post has no row any more, so neither the
/// cleanup nor the limit on pending uploads can touch an image someone published.
/// </remarks>
internal sealed class PostImageUpload(string imageKey, Guid ownerId, DateTimeOffset createdAt)
{
    public string ImageKey { get; private set; } = imageKey;

    public Guid OwnerId { get; private set; } = ownerId;

    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
}
