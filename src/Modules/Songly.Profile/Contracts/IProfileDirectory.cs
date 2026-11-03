namespace Songly.Profile.Contracts;

/// <summary>
/// The profile module's boundary for other modules (original Claude.md §2): display names, avatars
/// and favourite genres, without access to the module's entities.
/// </summary>
/// <remarks>
/// Every method takes a whole set and answers for all of it in one query, because the consumers
/// render whole pages — asking once per row would be N+1. Ids with nothing to report are simply
/// absent from the result. None of these filters suspended accounts or blocks: that is the
/// consumer's decision, made with data this module does not have (blocks) or for reasons only the
/// consumer knows.
/// </remarks>
public interface IProfileDirectory
{
    /// <summary>Unknown users are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Public avatar addresses. Users without an avatar are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> AvatarUrlsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Users with at least one of the given genres among their favourites.</summary>
    Task<IReadOnlySet<Guid>> UserIdsWithAnyGenreAsync(IReadOnlyCollection<Guid> genreIds, CancellationToken cancellationToken);

    /// <summary>The whole set of favourite genres per user. Users without any are absent.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>>> FavoriteGenreIdsByUserAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>
    /// How many users chose each of the given genres — a genre's popularity (original ADR 0029).
    /// Genres nobody chose are absent.
    /// </summary>
    /// <remarks>
    /// For weighting matches: a shared niche genre says far more about two people than a shared
    /// genre half the service picked. The numbers come from a cache (original ADR 0032) and may lag
    /// the database; for a rarity weight one person more on "Pop" changes nobody's result, but they
    /// must not be used where an exact, current number is needed.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, long>> GenreUserCountsAsync(IReadOnlyCollection<Guid> genreIds, CancellationToken cancellationToken);

    /// <summary>
    /// How many users have any favourite genre at all — the denominator of the popularity from
    /// <see cref="GenreUserCountsAsync"/>. Counted across the whole service, because "niche" means
    /// rare in the service, not rare in this sample. From the same cached snapshot as the counts.
    /// </summary>
    Task<long> UsersWithAnyGenreCountAsync(CancellationToken cancellationToken);
}
