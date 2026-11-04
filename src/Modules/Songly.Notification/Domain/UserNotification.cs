namespace Songly.Notification.Domain;

/// <summary>
/// One entry of a user's inbox (<c>notifications</c>). <see cref="ResourceId"/> points at the source —
/// a post, a request, a community — deliberately without a foreign key: those tables belong to other
/// modules, and a notification may outlive a deleted post.
/// </summary>
/// <remarks>Not called <c>Notification</c>: inside <c>Songly.Notification.*</c> that name is the module's namespace.</remarks>
internal sealed class UserNotification
{
    // For EF Core.
    private UserNotification()
    {
    }

    public UserNotification(Guid userId, NotificationType type, Guid actorId, Guid resourceId, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Type = type;
        ActorId = actorId;
        ResourceId = resourceId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>The recipient.</summary>
    public Guid UserId { get; private set; }

    public NotificationType Type { get; private set; }

    public Guid ActorId { get; private set; }

    public Guid ResourceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary><c>null</c> while unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>
    /// The next notification collapsed into this one (original ADR 0035): it points at the newest
    /// resource and comes back to the top of the inbox, as one.
    /// </summary>
    public void Refresh(Guid resourceId, DateTimeOffset occurredAt)
    {
        ResourceId = resourceId;
        CreatedAt = occurredAt;
    }

    /// <summary>The first reading counts; reading again keeps its time.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
