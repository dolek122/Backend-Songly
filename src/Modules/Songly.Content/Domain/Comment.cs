namespace Songly.Content.Domain;

internal sealed class Comment
{
    // For EF Core.
    private Comment() => Content = string.Empty;

    public Comment(Guid postId, Guid authorId, string content, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        PostId = postId;
        AuthorId = authorId;
        Content = content;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid PostId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Content { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
