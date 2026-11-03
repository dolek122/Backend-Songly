namespace Songly.Identity.Contracts;

/// <summary>
/// Which accounts are suspended right now — for modules that hide a suspended account's content
/// (original ADR 0026) and may not read the <c>users</c> table.
/// </summary>
/// <remarks>
/// <para>
/// A suspension used to cut the account off from writing and reading (original ADR 0022), but its
/// content stayed: someone was suspended for harassment, and the harassing posts — and the profile,
/// in the user search — stayed where everyone could see them.
/// </para>
/// <para>
/// The whole set, not a yes/no per user: consumers filter whole pages of results in the query, and
/// asking about every row separately would be N+1. The set is small by nature (an administrator
/// suspends by hand) and <b>holds no expired suspensions</b> — those count nowhere.
/// </para>
/// </remarks>
public interface ISuspendedAccounts
{
    Task<IReadOnlySet<Guid>> SuspendedUserIdsAsync(CancellationToken cancellationToken);
}
