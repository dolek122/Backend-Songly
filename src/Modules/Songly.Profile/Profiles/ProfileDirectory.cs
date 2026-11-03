using Microsoft.EntityFrameworkCore;
using Songly.Profile.Contracts;
using Songly.Profile.Persistence;

namespace Songly.Profile.Profiles;

internal sealed class ProfileDirectory(ProfileDbContext db, AvatarFiles avatars, GenrePopularityCache popularityCache) : IProfileDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await db.Profiles
            .Where(profile => userIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, profile => profile.DisplayName, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> AvatarUrlsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var withAvatar = await db.Profiles
            .AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId) && profile.AvatarKey != null)
            .ToListAsync(cancellationToken);
        return withAvatar.ToDictionary(profile => profile.UserId, profile => avatars.UrlOf(profile)!);
    }

    public async Task<IReadOnlySet<Guid>> UserIdsWithAnyGenreAsync(IReadOnlyCollection<Guid> genreIds, CancellationToken cancellationToken) =>
        (await db.UserGenres
            .Where(userGenre => genreIds.Contains(userGenre.GenreId))
            .Select(userGenre => userGenre.UserId)
            .Distinct()
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>>> FavoriteGenreIdsByUserAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var rows = await db.UserGenres
            .Where(userGenre => userIds.Contains(userGenre.UserId))
            .Select(userGenre => new { userGenre.UserId, userGenre.GenreId })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => row.UserId)
            .ToDictionary(group => group.Key, IReadOnlySet<Guid> (group) => group.Select(row => row.GenreId).ToHashSet());
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GenreUserCountsAsync(IReadOnlyCollection<Guid> genreIds, CancellationToken cancellationToken)
    {
        if (genreIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        // Narrowed to the genres asked about although the snapshot has them all: the contract
        // answers the question, it is not a dump of the table.
        var counts = (await PopularityAsync(cancellationToken)).Counts;
        return genreIds.Distinct().Where(counts.ContainsKey).ToDictionary(genreId => genreId, genreId => counts[genreId]);
    }

    public async Task<long> UsersWithAnyGenreCountAsync(CancellationToken cancellationToken) =>
        (await PopularityAsync(cancellationToken)).TotalUsers;

    /// <summary>
    /// The snapshot from Redis, or counted from the database and stored on a miss.
    /// </summary>
    /// <remarks>
    /// The two questions about popularity read the same entry, each with its own read. The entry is
    /// indivisible, so each sees numbers consistent with each other; a mismatch would need the key to
    /// expire exactly between the two reads of one request, and would give a weight from the
    /// neighbouring moment — not an error.
    /// </remarks>
    private async Task<GenrePopularity> PopularityAsync(CancellationToken cancellationToken)
    {
        if (await popularityCache.ReadAsync() is { } cached)
        {
            return cached;
        }

        var counts = await db.UserGenres
            .GroupBy(userGenre => userGenre.GenreId)
            .Select(group => new { GenreId = group.Key, Users = group.LongCount() })
            .ToDictionaryAsync(row => row.GenreId, row => row.Users, cancellationToken);
        var totalUsers = await db.UserGenres.Select(userGenre => userGenre.UserId).Distinct().LongCountAsync(cancellationToken);

        var snapshot = new GenrePopularity(counts, totalUsers);
        await popularityCache.WriteAsync(snapshot);
        return snapshot;
    }
}
