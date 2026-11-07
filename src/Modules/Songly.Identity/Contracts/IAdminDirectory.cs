namespace Songly.Identity.Contracts;

/// <summary>
/// "Who administers the platform", for other modules (original ADR 0023): the notification module has
/// to tell the administrators of an appeal sent to them, and has no business in the <c>users</c> table.
/// Ids only — a consumer needs recipients, exactly as with a community's moderators.
/// </summary>
public interface IAdminDirectory
{
    Task<IReadOnlySet<Guid>> AdminIdsAsync(CancellationToken cancellationToken);
}
