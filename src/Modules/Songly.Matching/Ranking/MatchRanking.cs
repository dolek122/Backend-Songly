namespace Songly.Matching.Ranking;

/// <summary>One proposed match, before the display data is looked up.</summary>
/// <param name="SharedGenreIds">Ordered by their text form, as the original ordered them.</param>
internal sealed record ScoredCandidate(Guid UserId, double Score, double Confidence, IReadOnlyList<Guid> SharedGenreIds);

/// <summary>
/// The arithmetic of matching, with no database and no other module: a <b>weighted</b> Jaccard
/// coefficient (original ADR 0029) damped by the amount of data (original ADR 0033). Kept apart from
/// <see cref="MatchingService"/> so it can be tested on exact inputs, the way the original's
/// <c>MatchingServiceTest</c> fed it popularity.
/// </summary>
/// <remarks>
/// <para>
/// The weight is a genre's rarity. A raw Jaccard treated "Pop", picked by half the service, exactly
/// like "Dark Ambient", picked by two people — and a shared "Pop" says nothing about anyone, so
/// everyone with mainstream taste looked equally well matched to everyone else.
/// </para>
/// <para>
/// The coefficient alone measures the <i>proportion</i> covered, not how much we know: two people with
/// one genre in common scored 1.0 — a perfect result from a single data point, above someone sharing
/// seven genres of ten. Hence the damping.
/// </para>
/// </remarks>
internal static class MatchRanking
{
    /// <summary>The genres whose popularity <see cref="Rank"/> needs: mine and every candidate's.</summary>
    public static IReadOnlyCollection<Guid> RelevantGenres(IReadOnlySet<Guid> myGenres, IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> genresByCandidate)
    {
        var relevant = new HashSet<Guid>(myGenres);
        foreach (var genres in genresByCandidate.Values)
        {
            relevant.UnionWith(genres);
        }

        return relevant;
    }

    /// <summary>
    /// Every candidate scored, best first; equal scores in the order of the user ids' text form, so
    /// the order — and with it every page — is the same on every request.
    /// </summary>
    /// <param name="genreUserCounts">How many users picked each genre; genres missing count as one.</param>
    /// <param name="usersWithAnyGenre">How many users picked any genre — the whole service, not the candidates.</param>
    public static IReadOnlyList<ScoredCandidate> Rank(
        IReadOnlySet<Guid> myGenres,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> genresByCandidate,
        IReadOnlyDictionary<Guid, long> genreUserCounts,
        long usersWithAnyGenre,
        double confidencePrior)
    {
        var weights = Weights(RelevantGenres(myGenres, genresByCandidate), genreUserCounts, usersWithAnyGenre);
        return
        [
            .. genresByCandidate
                .Select(candidate => Score(candidate.Key, candidate.Value, myGenres, weights, confidencePrior))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.UserId.ToString(), StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// A genre's weight: <c>ln(1 + N / n)</c>, where <c>N</c> is the number of users with any genre and
    /// <c>n</c> the number who picked this one (original ADR 0029).
    /// </summary>
    /// <remarks>
    /// The shape is deliberate. The weight <b>never falls to zero</b>, so a shared mainstream genre
    /// still counts for something (at <c>n = N</c> it is <c>ln 2</c>), and it <b>never runs off to
    /// infinity</b>, so a single rarity does not eclipse the whole profile — the logarithm grows slowly
    /// and the one under it guards against <c>ln 0</c>. Popularity is counted across the service, not
    /// among the candidates: "niche" has to mean "rare in the service", or the weights would depend on
    /// who asks, and two people would see different matches to each other.
    /// </remarks>
    private static Dictionary<Guid, double> Weights(IReadOnlyCollection<Guid> genres, IReadOnlyDictionary<Guid, long> genreUserCounts, long usersWithAnyGenre)
    {
        var total = Math.Max(1, usersWithAnyGenre);
        return genres.ToDictionary(
            genreId => genreId,
            genreId => Math.Log(1.0 + ((double)total / Math.Max(1, genreUserCounts.GetValueOrDefault(genreId, 1)))));
    }

    /// <summary>
    /// The weighted Jaccard damped by the amount of data (original ADRs 0029 and 0033): the weight of the
    /// intersection over the weight of the union <b>plus a constant</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The denominator stays the union, not the number of shared genres: whoever picked everything is a
    /// weak match to everyone, because their set says nothing about their taste.
    /// </para>
    /// <para>
    /// <b>A constant in the denominator, not a separate factor.</b> Confidence is "how much the two told
    /// about themselves": the union's weight against that weight plus the constant. The score times that
    /// confidence reduces to a single addition in the denominator — classic smoothing, with no second
    /// parameter and no second branch.
    /// </para>
    /// <para>
    /// <b>Confidence counts the union, not the intersection</b> — that is the point. Counted on the
    /// intersection, "ten genres each, one in common" would read as little data, when it is a case we
    /// know a lot about and the answer is "you do not match". Distrust is owed to small sets, not to
    /// weak results.
    /// </para>
    /// </remarks>
    private static ScoredCandidate Score(Guid candidateId, IReadOnlySet<Guid> candidateGenres, IReadOnlySet<Guid> myGenres, Dictionary<Guid, double> weights, double confidencePrior)
    {
        var shared = candidateGenres.Where(myGenres.Contains).ToList();
        var unionWeight = WeightOf(candidateGenres.Union(myGenres), weights);
        var damped = unionWeight + confidencePrior;
        return new ScoredCandidate(
            candidateId,
            WeightOf(shared, weights) / damped,
            unionWeight / damped,
            [.. shared.OrderBy(genreId => genreId.ToString(), StringComparer.Ordinal)]);
    }

    private static double WeightOf(IEnumerable<Guid> genres, Dictionary<Guid, double> weights) =>
        genres.Sum(genreId => weights.GetValueOrDefault(genreId, 1.0));
}
