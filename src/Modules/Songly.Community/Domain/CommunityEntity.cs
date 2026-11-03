namespace Songly.Community.Domain;

/// <summary>A topic community. Public: anyone logged in can see it and join it at once.</summary>
/// <remarks>
/// The original's <c>Community</c>; renamed because a class called <c>Community</c> inside the
/// <c>Songly.Community</c> namespace would be ambiguous with it.
/// </remarks>
internal sealed class CommunityEntity
{
    // For EF Core.
    private CommunityEntity() => Name = string.Empty;

    public CommunityEntity(string name, string? description, Guid ownerId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        OwnerId = ownerId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>
    /// Ownership is this column, not a rank on the role scale: one source of truth for who the owner
    /// is. The owner's membership row carries <see cref="CommunityRole.Owner"/> so that lists show it.
    /// </summary>
    public Guid OwnerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}
