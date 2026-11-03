namespace Songly.Social.Domain;

/// <summary>
/// A request in one direction. At most one is pending per direction (a partial unique index); the
/// history — accepted and declined ones — is kept.
/// </summary>
internal sealed class FriendRequest
{
    // For EF Core.
    private FriendRequest()
    {
    }

    public FriendRequest(Guid requesterId, Guid addresseeId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        RequesterId = requesterId;
        AddresseeId = addresseeId;
        Status = FriendRequestStatus.Pending;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid AddresseeId { get; private set; }

    public FriendRequestStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public void Accept(DateTimeOffset now)
    {
        Status = FriendRequestStatus.Accepted;
        RespondedAt = now;
    }

    public void Decline(DateTimeOffset now)
    {
        Status = FriendRequestStatus.Declined;
        RespondedAt = now;
    }
}
