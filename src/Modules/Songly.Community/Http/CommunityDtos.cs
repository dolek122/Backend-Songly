using Songly.Common.Validation;

namespace Songly.Community.Http;

internal enum CommunityRoleDto
{
    Owner,
    Moderator,
    Member,
}

/// <summary>Mutually exclusive: a ban removes the membership, and an expired ban counts nowhere.</summary>
internal enum CommunityMembershipStatus
{
    Member,
    Banned,
    None,
}

internal sealed record CreateCommunityRequest
{
    [NotBlank]
    [Size(0, 100)]
    public string? Name { get; init; }

    [Size(0, 500)]
    public string? Description { get; init; }
}

internal sealed record UpdateCommunityRequest
{
    [NotBlank]
    [Size(0, 100)]
    public string? Name { get; init; }

    [Size(0, 500)]
    public string? Description { get; init; }
}

/// <summary>
/// <c>OWNER</c> is accepted by the type and refused by the service: ownership is a column of its
/// own, not a rank on the same scale, and a second enum differing by one value would cost more than
/// one explicit check.
/// </summary>
internal sealed record SetMemberRoleRequest
{
    [NotNull]
    public CommunityRoleDto? Role { get; init; }
}

/// <param name="MyRole"><c>null</c> when the viewer is not a member.</param>
internal sealed record CommunityResponse(
    Guid Id,
    string Name,
    string? Description,
    Guid OwnerId,
    string? OwnerDisplayName,
    string? OwnerAvatarUrl,
    long MemberCount,
    CommunityRoleDto? MyRole,
    DateTimeOffset CreatedAt);

internal sealed record CommunityMemberResponse(Guid UserId, string? DisplayName, string? AvatarUrl, CommunityRoleDto Role, DateTimeOffset JoinedAt);

/// <summary>
/// The caller's own standing in a community (original ADR 0015). Each field means something only for
/// its status: <c>role</c> and <c>joinedAt</c> for <c>MEMBER</c>, <c>ban</c> for <c>BANNED</c>.
/// </summary>
/// <param name="Ban">Always <c>null</c> until bans arrive (the next part); the field is in the contract already.</param>
internal sealed record MembershipStatusResponse(CommunityMembershipStatus Status, CommunityRoleDto? Role, DateTimeOffset? JoinedAt, object? Ban);
