namespace Songly.Identity.Http;

/// <summary>
/// The account's own suspension (original ADR 0022) — the one place a suspended user learns the
/// reason. The reason travels only this way, never by e-mail: a push channel would turn a cycle of
/// suspensions into delivering arbitrary administrator text to someone's inbox.
/// </summary>
/// <remarks>
/// For an active account <see cref="Suspended"/> is <c>false</c> and every other field
/// <c>null</c> — not a 404, because the client asks after every <c>ACCOUNT_SUSPENDED</c>.
/// <see cref="AppealStatus"/> <c>null</c> while suspended means "may appeal". No appeal text: the
/// author knows it, and this is the state of the case, not its file.
/// </remarks>
internal sealed record SuspensionResponse(
    bool Suspended,
    string? Reason,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? ExpiresAt,
    AppealStatus? AppealStatus,
    DateTimeOffset? AppealedAt)
{
    public static readonly SuspensionResponse NotSuspended = new(false, null, null, null, null, null);
}
