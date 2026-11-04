namespace Songly.Community.Contracts;

/// <summary>A new membership — not joining again while already a member.</summary>
public sealed record CommunityMemberJoined(Guid CommunityId, Guid OwnerId, Guid MemberId, DateTimeOffset OccurredAt);

/// <summary>
/// The owner changed someone's role in the community (original ADR 0016) — published only on an
/// <b>actual</b> change: setting the role a member already has changes nothing, like banning someone
/// already banned (ADR 0014), so a repeated click does not notify again.
/// </summary>
/// <remarks>
/// The direction travels as <paramref name="ModerationGranted"/>, not as a role: the roles are this
/// module's own vocabulary, and a consumer has no business with them. With the only reachable changes
/// being member ↔ moderator (owner cannot be assigned), the flag carries all of it.
/// </remarks>
/// <param name="ChangedBy">The owner who made the change.</param>
public sealed record CommunityMemberRoleChanged(Guid CommunityId, Guid UserId, Guid ChangedBy, bool ModerationGranted, DateTimeOffset OccurredAt);
