namespace Songly.Identity.Contracts;

/// <summary>
/// The platform's administrator role was granted or revoked (original ADR 0031) — only on an actual change:
/// granting the role to someone who has it is no change and publishes nothing.
/// </summary>
/// <param name="ActorId">The administrator who made the change.</param>
public sealed record AdminRoleChanged(Guid UserId, Guid ActorId, bool Granted, DateTimeOffset OccurredAt);
