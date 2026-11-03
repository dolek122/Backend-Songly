namespace Songly.Content.Domain;

/// <summary>
/// A post — an ordinary one, or one published in a community. One table for both on purpose
/// (original Claude.md §8): a post is the same thing wherever it is published, and a separate
/// <c>community_posts</c> would duplicate likes, comments and shares together with everything that
/// handles them.
/// </summary>
internal sealed class Post
{
    // For EF Core.
    private Post() => Content = string.Empty;

    public Post(Guid authorId, string content, DateTimeOffset now, Guid? communityId, string? imageKey)
    {
        Id = Guid.NewGuid();
        AuthorId = authorId;
        Content = content;
        CreatedAt = now;
        CommunityId = communityId;
        ImageKey = imageKey;
    }

    public Guid Id { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Content { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// <c>null</c> for an ordinary post. A post in a community belongs to it, not to the author's
    /// timeline: it reaches neither the friends' feed nor the author's profile.
    /// </summary>
    public Guid? CommunityId { get; private set; }

    /// <summary>The key of the image in object storage, not its URL (original ADR 0010); <c>null</c> without an image.</summary>
    public string? ImageKey { get; private set; }
}
