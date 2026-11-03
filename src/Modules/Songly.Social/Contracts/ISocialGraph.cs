namespace Songly.Social.Contracts;

/// <summary>
/// The social module's boundary for other modules (original Claude.md §2): who is friends with whom,
/// and who blocks whom, without access to the module's entities. The feed reads friends; content,
/// notifications and matching read blocks.
/// </summary>
public interface ISocialGraph
{
    Task<IReadOnlySet<Guid>> FriendIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A block in either direction hides the two users from each other.</summary>
    Task<bool> IsBlockedEitherWayAsync(Guid userId, Guid otherUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Everyone linked to <paramref name="userId"/> by a block in either direction — the set content
    /// filters whole pages with, in the query.
    /// </summary>
    Task<IReadOnlySet<Guid>> BlockedEitherWayIdsAsync(Guid userId, CancellationToken cancellationToken);
}
