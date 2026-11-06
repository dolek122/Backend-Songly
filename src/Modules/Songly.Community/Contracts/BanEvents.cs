namespace Songly.Community.Contracts;

/// <summary>
/// A ban that changes the state from "not banned" to "banned": the first one, or one overwriting an
/// expired ban (original ADR 0014). Not a repeated ban of someone banned already — a ban is idempotent,
/// and without this rule every repeated click of a moderator would tell the banned user again.
/// </summary>
/// <param name="ExpiresAt"><c>null</c> for a ban without a term. The e-mail tells the banned user how long it lasts.</param>
public sealed record CommunityMemberBanned(Guid CommunityId, Guid UserId, Guid ModeratorId, DateTimeOffset? ExpiresAt, DateTimeOffset OccurredAt);

/// <summary>
/// The banned user appealed (original ADR 0017). At most once per ban: the appeal is one, a second is
/// a 409, so the anti-spam gate is at the publisher, as everywhere.
/// </summary>
public sealed record CommunityBanAppealSubmitted(Guid CommunityId, Guid UserId, DateTimeOffset OccurredAt);

/// <summary>
/// The appeal accepted — that is, an active ban with a pending appeal lifted (original ADR 0017). There is
/// no separate "accept" endpoint: accepting the appeal and lifting the ban are the same decision.
/// </summary>
public sealed record CommunityBanAppealAccepted(Guid CommunityId, Guid UserId, Guid ModeratorId, DateTimeOffset OccurredAt);

/// <summary>The appeal rejected, the ban stays (original ADR 0017). Final for this ban at the first instance.</summary>
public sealed record CommunityBanAppealRejected(Guid CommunityId, Guid UserId, Guid ModeratorId, DateTimeOffset OccurredAt);

/// <summary>
/// The banned user sent a rejected appeal to the <b>platform's administration</b> (original ADR 0028).
/// Every administrator is told: they have no queue in the app they would look into by themselves. At most
/// once per ban — escalating is possible only from "rejected", and that state is left for good.
/// </summary>
public sealed record CommunityBanAppealEscalated(Guid CommunityId, Guid UserId, DateTimeOffset OccurredAt);

/// <summary>
/// The platform's administration overturned a community's ban (original ADR 0028). A separate event from
/// <see cref="CommunityBanAppealAccepted"/> despite the same effect: there the community's own moderation
/// lifted the ban, here <b>someone from outside</b> did, and a message saying "the moderation accepted
/// your appeal" would simply be untrue.
/// </summary>
public sealed record CommunityBanEscalationAccepted(Guid CommunityId, Guid UserId, Guid AdminId, DateTimeOffset OccurredAt);

/// <summary>The administration kept the ban (original ADR 0028) — the end of the ladder: there is no third instance.</summary>
public sealed record CommunityBanEscalationRejected(Guid CommunityId, Guid UserId, Guid AdminId, DateTimeOffset OccurredAt);
