namespace Songly.Identity.Contracts;

/// <summary>
/// Every session of the account was revoked — a password change, a reset, or a stolen refresh token detected.
/// Published inside the revocation's transaction where there is one, so what other modules cut off with the
/// sessions commits or rolls back together with them.
/// </summary>
/// <remarks>
/// The notification module unpins the account's phones (original ADR 0040): a password change after a stolen
/// phone that leaves friends' names ringing on it closes nothing — the same argument that makes the change
/// revoke every session.
/// </remarks>
public sealed record UserSessionsRevoked(Guid UserId, DateTimeOffset OccurredAt);
