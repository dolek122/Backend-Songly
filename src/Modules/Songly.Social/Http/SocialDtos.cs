using Songly.Common.Validation;

namespace Songly.Social.Http;

internal enum FriendRequestStatusDto
{
    Pending,
    Accepted,
    Declined,
}

internal enum FriendRequestDirection
{
    Incoming,
    Outgoing,
}

internal sealed record SendFriendRequestRequest
{
    [NotNull]
    public Guid? AddresseeId { get; init; }
}

internal sealed record FriendRequestResponse(
    Guid Id,
    Guid RequesterId,
    string? RequesterDisplayName,
    string? RequesterAvatarUrl,
    Guid AddresseeId,
    string? AddresseeDisplayName,
    string? AddresseeAvatarUrl,
    FriendRequestStatusDto Status,
    DateTimeOffset CreatedAt);

internal sealed record FriendResponse(Guid UserId, string? DisplayName, string? AvatarUrl, DateTimeOffset FriendsSince);

internal sealed record BlockedUserResponse(Guid UserId, string? DisplayName, DateTimeOffset BlockedAt);

/// <summary>
/// Whether the caller blocks the user (original ADR 0039). This direction only: "do they block me"
/// would reveal someone else's decision, which the rest of the API deliberately hides behind 404.
/// </summary>
internal sealed record BlockStatusResponse(bool Blocked);
