using Microsoft.Extensions.Options;
using Songly.Common.Paging;
using Songly.Identity.Contracts;
using Songly.Matching.Http;
using Songly.Matching.Ranking;
using Songly.Profile.Contracts;
using Songly.Social.Contracts;

namespace Songly.Matching;

/// <summary>
/// Genre-based matching, computed on request — no <c>match_scores</c> table and no machine learning
/// until there is behavioural data to justify one (original Claude.md §8). The arithmetic is
/// <see cref="MatchRanking"/>; this class picks the candidates and fetches what it needs.
/// </summary>
internal sealed class MatchingService(
    IProfileDirectory profiles,
    ISocialGraph socialGraph,
    ISuspendedAccounts suspendedAccounts,
    IOptions<MatchingOptions> options)
{
    public async Task<PagedResponse<MatchResponse>> MatchesAsync(Guid userId, PageQuery page, CancellationToken cancellationToken)
    {
        var myGenres = (await profiles.FavoriteGenreIdsByUserAsync([userId], cancellationToken)).GetValueOrDefault(userId);
        if (myGenres is null || myGenres.Count == 0)
        {
            return PagingExtensions.Empty<MatchResponse>(page);
        }

        var candidateIds = new HashSet<Guid>(await profiles.UserIdsWithAnyGenreAsync([.. myGenres], cancellationToken));
        candidateIds.Remove(userId);
        candidateIds.ExceptWith(await socialGraph.FriendIdsAsync(userId, cancellationToken));
        candidateIds.ExceptWith(await socialGraph.BlockedEitherWayIdsAsync(userId, cancellationToken));

        // Suspended accounts are not proposed (original ADR 0029, a gap after ADR 0026): a match is an
        // invitation to get in touch, and proposing contact with someone the platform took the service
        // away from — usually for harassment — harms the person it is proposed to.
        candidateIds.ExceptWith(await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken));
        if (candidateIds.Count == 0)
        {
            return PagingExtensions.Empty<MatchResponse>(page);
        }

        var genresByCandidate = await profiles.FavoriteGenreIdsByUserAsync(candidateIds, cancellationToken);
        var counts = await profiles.GenreUserCountsAsync(MatchRanking.RelevantGenres(myGenres, genresByCandidate), cancellationToken);
        var total = await profiles.UsersWithAnyGenreCountAsync(cancellationToken);
        var ranked = MatchRanking.Rank(myGenres, genresByCandidate, counts, total, options.Value.ConfidencePrior);

        var slice = page.Offset >= ranked.Count ? [] : ranked.Skip((int)page.Offset).Take(page.Size).ToList();
        return PagingExtensions.Page(await WithProfilesAsync(slice, cancellationToken), page, ranked.Count);
    }

    /// <summary>
    /// Names and avatars are looked up <b>after</b> the page is cut. There are as many candidates as
    /// people who picked any of my genres — with "Pop", half the service — and twenty are shown; asking
    /// for all of them meant two queries with thousands of ids on every request.
    /// </summary>
    private async Task<IReadOnlyList<MatchResponse>> WithProfilesAsync(List<ScoredCandidate> slice, CancellationToken cancellationToken)
    {
        if (slice.Count == 0)
        {
            return [];
        }

        var userIds = slice.Select(candidate => candidate.UserId).ToList();
        var names = await profiles.DisplayNamesAsync(userIds, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(userIds, cancellationToken);
        return
        [
            .. slice.Select(candidate => new MatchResponse(
                candidate.UserId,
                names.GetValueOrDefault(candidate.UserId),
                avatars.GetValueOrDefault(candidate.UserId),
                candidate.Score,
                candidate.Confidence,
                candidate.SharedGenreIds)),
        ];
    }
}
