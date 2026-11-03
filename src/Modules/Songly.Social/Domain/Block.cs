namespace Songly.Social.Domain;

/// <summary>One direction: <see cref="BlockerId"/> blocks <see cref="BlockedId"/>.</summary>
internal sealed class Block
{
    // For EF Core.
    private Block()
    {
    }

    public Block(Guid blockerId, Guid blockedId, DateTimeOffset createdAt)
    {
        BlockerId = blockerId;
        BlockedId = blockedId;
        CreatedAt = createdAt;
    }

    public Guid BlockerId { get; private set; }

    public Guid BlockedId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
