namespace Songly.Community.Contracts;

/// <summary>
/// The community module's boundary for questions about membership and permissions (original
/// Claude.md §2) — for content, which keeps posts in communities and must know who may write and
/// moderate there, but may not read the membership table.
/// </summary>
public interface ICommunityMembership
{
    Task<bool> ExistsAsync(Guid communityId, CancellationToken cancellationToken);

    Task<bool> IsMemberAsync(Guid communityId, Guid userId, CancellationToken cancellationToken);

    Task<bool> IsOwnerAsync(Guid communityId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The owner or a moderator (original ADR 0011). Separate from <see cref="IsOwnerAsync"/>,
    /// because "who may delete someone else's post" (moderators) is not "who may hand out roles and
    /// delete the whole community" (the owner alone) — and confusing the two is what this prevents.
    /// </summary>
    Task<bool> CanModerateAsync(Guid communityId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Who moderates the community — the owner and the moderators. For notifying them about a report.</summary>
    Task<IReadOnlySet<Guid>> ModeratorIdsAsync(Guid communityId, CancellationToken cancellationToken);
}
