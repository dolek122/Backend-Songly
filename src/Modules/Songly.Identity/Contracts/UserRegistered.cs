namespace Songly.Identity.Contracts;

/// <summary>
/// A new account exists — the only way other modules learn about one (the profile module creates
/// the profile), without reaching for identity's <c>users</c> table.
/// </summary>
/// <remarks>
/// <para>
/// Published <b>inside</b> the transaction that creates the account, after its row: handlers are
/// part of that transaction, so an account and everything other modules set up for it exist
/// together or not at all. The original published it after the commit instead
/// (<c>@TransactionalEventListener(AFTER_COMMIT)</c>), where a failing handler was only logged —
/// and left an account without a profile: <c>/profiles/me</c> answering 404 forever, and nobody
/// able to find the user. Here the confirmation fails as a whole, and since the pending
/// registration is rolled back too, the same link can simply be opened again.
/// </para>
/// <para>
/// <b>No e-mail address</b> (original ADR 0046). The original's event once carried it only so that
/// the profile could make a default display name out of it, and that name published part of the
/// address to everyone. A domain event is not a way for an address to spread through the
/// application.
/// </para>
/// </remarks>
public sealed record UserRegistered(Guid UserId, DateTimeOffset OccurredAt);
