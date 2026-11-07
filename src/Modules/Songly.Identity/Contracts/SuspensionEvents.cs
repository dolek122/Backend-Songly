namespace Songly.Identity.Contracts;

/// <summary>
/// An account was suspended (original ADR 0022) — published only on the change from active to suspended, not
/// when an ongoing suspension is re-decided: the same rule as "only a new ban notifies" (ADR 0014).
/// </summary>
/// <remarks>
/// It carries the term but <b>not the reason</b>: the reason is an administrator's text, and push channels do
/// not carry it (ADR 0015). The suspended user comes for it to <c>GET /api/v1/auth/me/suspension</c>.
/// </remarks>
public sealed record UserSuspended(Guid UserId, Guid ActorId, DateTimeOffset? ExpiresAt, DateTimeOffset OccurredAt);

/// <summary>
/// A suspension lifted before its term (original ADR 0022). Not published when it expires — expiry is lazy,
/// there is no moment anything happens — so only someone a person restored hears of it.
/// </summary>
public sealed record UserSuspensionLifted(Guid UserId, Guid ActorId, DateTimeOffset OccurredAt);

/// <summary>
/// The suspended user appealed (original ADR 0023). Every administrator is told: the list of suspensions is
/// a tool nothing starts without it, and the appeal comes from the user, so nobody else knows of it. At most
/// once per suspension — the volume is limited by the data, not by an interval.
/// </summary>
public sealed record UserSuspensionAppealSubmitted(Guid UserId, DateTimeOffset OccurredAt);

/// <summary>
/// The appeal accepted — a suspension with a pending appeal lifted (original ADR 0023). It <b>replaces</b>
/// <see cref="UserSuspensionLifted"/> then, so the appellant gets one message saying <i>why</i> the account is
/// back, not two about the same thing.
/// </summary>
public sealed record UserSuspensionAppealAccepted(Guid UserId, Guid ActorId, DateTimeOffset OccurredAt);

/// <summary>The appeal rejected — the suspension stays (original ADR 0023). Final for this suspension.</summary>
public sealed record UserSuspensionAppealRejected(Guid UserId, Guid ActorId, DateTimeOffset OccurredAt);
