namespace Songly.Social.Contracts;

/// <summary>A friend request was sent and is waiting for the addressee.</summary>
public sealed record FriendRequestSent(Guid RequestId, Guid RequesterId, Guid AddresseeId, DateTimeOffset OccurredAt);

/// <summary>
/// A friend request was accepted — also when it happens by itself, because the addressee had already
/// sent one the other way: both people asked for the friendship, and the one asked first is told.
/// </summary>
public sealed record FriendRequestAccepted(Guid RequestId, Guid RequesterId, Guid AddresseeId, DateTimeOffset OccurredAt);
