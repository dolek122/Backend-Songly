using Songly.Matching.Ranking;

namespace Songly.Matching.Tests;

/// <summary>
/// The arithmetic on exact inputs. Ported from the scoring scenarios of the original
/// <c>MatchingServiceTest</c>; who becomes a candidate (friends, blocks, suspensions) and the paging
/// are the service's, tested through the API in <c>MatchingTests</c>.
/// </summary>
/// <remarks>
/// Until the original ADR 0033, equal popularity made exact numbers assertable: the weights cancelled
/// out and a raw Jaccard was left. Not any more — the confidence constant cancels with nothing, and the
/// weight is a logarithm, so no popularity gives a round result. The tests check order, ranges and
/// differences, not magic constants that would need recomputing whenever the prior changes.
/// </remarks>
public sealed class MatchRankingTests
{
    private const double Prior = 2.0;

    private static readonly Guid G1 = Guid.NewGuid();
    private static readonly Guid G2 = Guid.NewGuid();
    private static readonly Guid G3 = Guid.NewGuid();
    private static readonly Guid G4 = Guid.NewGuid();

    [Fact]
    public void Candidates_are_ranked_by_their_score()
    {
        var similar = Guid.NewGuid();
        var lessSimilar = Guid.NewGuid();

        var ranked = RankEquallyPopular([G1, G2, G3], (similar, [G1, G2, G4]), (lessSimilar, [G3]));

        // similar shares two genres of the four in the union, lessSimilar one of three.
        Assert.Equal([similar, lessSimilar], ranked.Select(match => match.UserId));
        Assert.Equal(new[] { G1, G2 }.OrderBy(id => id.ToString(), StringComparer.Ordinal), ranked[0].SharedGenreIds);
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    /// <summary>Identical tastes are no longer 1.0 (original ADR 0033): one shared genre is one data point, not proof of twins.</summary>
    [Fact]
    public void Identical_tastes_score_below_one()
    {
        var twin = Guid.NewGuid();

        var match = Assert.Single(RankEquallyPopular([G1], (twin, [G1])));

        Assert.InRange(match.Score, double.Epsilon, 1.0 - 1e-9);
    }

    /// <summary>
    /// The point of the original ADR 0029: with the <b>same</b> number of shared genres and the same set
    /// sizes, the one we share a <b>niche</b> genre with ranks higher. A raw Jaccard tied them, and the id
    /// decided the order.
    /// </summary>
    [Fact]
    public void A_shared_rare_genre_ranks_above_a_shared_mainstream_one()
    {
        var niche = G1;
        var mainstream = G2;
        var nicheTwin = Guid.NewGuid();
        var mainstreamTwin = Guid.NewGuid();

        // "Dark Ambient" picked by 2 people, "Pop" by 900 of a thousand; G3 and G4 by 10 each.
        var ranked = MatchRanking.Rank(
            Set(niche, mainstream),
            Candidates((nicheTwin, [niche, G3]), (mainstreamTwin, [mainstream, G4])),
            new Dictionary<Guid, long> { [niche] = 2, [mainstream] = 900, [G3] = 10, [G4] = 10 },
            usersWithAnyGenre: 1000,
            Prior);

        Assert.Equal([nicheTwin, mainstreamTwin], ranked.Select(match => match.UserId));
        Assert.True(ranked[0].Score > ranked[1].Score);
        Assert.All(ranked, match => Assert.InRange(match.Score, double.Epsilon, 1.0 - 1e-9));
    }

    /// <summary>
    /// The point of the original ADR 0033: a richer profile with a <b>smaller</b> proportion covered is
    /// not beaten as surely as the proportion alone says by a tiny one covered completely. Before, two
    /// people with one genre in common scored 1.0 and stood above someone sharing three of four.
    /// </summary>
    [Fact]
    public void A_tiny_identical_profile_does_not_win_on_the_proportion_alone()
    {
        var twin = Guid.NewGuid();
        var richCandidate = Guid.NewGuid();

        var ranked = RankEquallyPopular([G1], (twin, [G1]), (richCandidate, [G1, G2, G3, G4]));

        Assert.All(ranked, match => Assert.InRange(match.Confidence, double.Epsilon, 1.0 - 1e-9));
        var twinMatch = ranked.Single(match => match.UserId == twin);
        var richMatch = ranked.Single(match => match.UserId == richCandidate);
        Assert.True(richMatch.Confidence > twinMatch.Confidence);

        // The small union eats most of the score: full coverage on one genre does not reach even 0.6.
        Assert.True(twinMatch.Score < 0.6, $"score {twinMatch.Score}");
    }

    /// <summary>
    /// Confidence grows with the amount of data, not with the quality of the match — which is why it
    /// counts the <b>union</b>, not the intersection (original ADR 0033). "Four genres each, one shared" is
    /// a lot of data and a weak result, not little data.
    /// </summary>
    [Fact]
    public void Confidence_follows_how_much_both_sides_told()
    {
        var poorlyKnown = Guid.NewGuid();
        var wellKnown = Guid.NewGuid();

        var ranked = RankEquallyPopular([G1, G2], (poorlyKnown, [G1]), (wellKnown, [G1, G3, G4]));

        var poor = ranked.Single(match => match.UserId == poorlyKnown);
        var well = ranked.Single(match => match.UserId == wellKnown);

        // Both share exactly one genre with the viewer, but wellKnown told more, so we know more about
        // their result — higher confidence...
        Assert.True(well.Confidence > poor.Confidence);

        // ...and at the same time a LOWER score: the same single shared genre means less in a bigger
        // union. Confidence and score are two different things.
        Assert.True(well.Score < poor.Score);
    }

    /// <summary>
    /// Whoever picked everything is a weak match to everyone: the denominator is the union, so a set
    /// covering mine and much more says little about taste.
    /// </summary>
    [Fact]
    public void Picking_everything_does_not_make_a_strong_match()
    {
        var focused = Guid.NewGuid();
        var everything = Guid.NewGuid();

        var ranked = RankEquallyPopular([G1], (focused, [G1, G2]), (everything, [G1, G2, G3, G4]));

        Assert.True(
            ranked.Single(match => match.UserId == focused).Score > ranked.Single(match => match.UserId == everything).Score,
            "the same shared genre in a bigger union must score lower");
    }

    /// <summary>Equal scores go in a fixed order — the user ids' text form — or the pages of one list would differ between requests.</summary>
    [Fact]
    public void Ties_are_broken_by_the_user_id()
    {
        var candidates = Enumerable.Range(0, 20).Select(_ => (Guid.NewGuid(), new[] { G1 })).ToArray();

        var ranked = RankEquallyPopular([G1], candidates);

        Assert.Equal(candidates.Select(c => c.Item1.ToString()).Order(StringComparer.Ordinal), ranked.Select(match => match.UserId.ToString()));
    }

    /// <summary>A prior of zero is the behaviour before the original ADR 0033 — proportion alone, and a perfect score from one genre.</summary>
    [Fact]
    public void Without_the_prior_identical_tastes_score_one()
    {
        var twin = Guid.NewGuid();

        var match = Assert.Single(MatchRanking.Rank(Set(G1), Candidates((twin, [G1])), EqualCounts(G1), 100, confidencePrior: 0));

        Assert.Equal(1.0, match.Score, precision: 12);
        Assert.Equal(1.0, match.Confidence, precision: 12);
    }

    private static IReadOnlyList<ScoredCandidate> RankEquallyPopular(Guid[] myGenres, params (Guid UserId, Guid[] Genres)[] candidates) =>
        MatchRanking.Rank(Set(myGenres), Candidates(candidates), EqualCounts(G1, G2, G3, G4), usersWithAnyGenre: 100, Prior);

    /// <summary>Every genre equally popular — the same weight for all.</summary>
    private static Dictionary<Guid, long> EqualCounts(params Guid[] genres) => genres.ToDictionary(id => id, _ => 10L);

    private static HashSet<Guid> Set(params Guid[] genres) => [.. genres];

    private static Dictionary<Guid, IReadOnlySet<Guid>> Candidates(params (Guid UserId, Guid[] Genres)[] candidates) =>
        candidates.ToDictionary(c => c.UserId, c => (IReadOnlySet<Guid>)Set(c.Genres));
}
