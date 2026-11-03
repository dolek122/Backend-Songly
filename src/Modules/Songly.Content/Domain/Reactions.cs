namespace Songly.Content.Domain;

/// <summary>
/// A like: one per user and post (the primary key), so liking again is no change. A pure number
/// for everyone but the liker — it carries no text, which is why blocks hide comments from the
/// counts but not likes (original Claude.md §9).
/// </summary>
internal sealed class Like(Guid postId, Guid userId, DateTimeOffset createdAt)
{
    public Guid PostId { get; private set; } = postId;

    public Guid UserId { get; private set; } = userId;

    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
}

/// <summary>A share: one per user and post. It brings the post into the feed of the sharer's friends.</summary>
internal sealed class Share(Guid postId, Guid userId, DateTimeOffset createdAt)
{
    public Guid PostId { get; private set; } = postId;

    public Guid UserId { get; private set; } = userId;

    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
}
