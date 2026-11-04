namespace Songly.Matching.Http;

/// <summary>One proposed match.</summary>
/// <param name="Score">
/// The strength of the match, in (0, 1) — <b>what the list is sorted by</b>, and the number to show. The
/// weighted Jaccard (original ADR 0029) times the confidence (ADR 0033): the weight of the shared genres
/// over the weight of the union <i>plus a constant</i>. 1.0 cannot be reached, on purpose — nobody's
/// taste is known completely.
/// </param>
/// <param name="Confidence">
/// How much of the score is data and how much guesswork: near 0 for practically empty profiles, near 1
/// for rich ones. Exposed so that a client can avoid showing a percentage nothing backs ("based on one
/// genre") — <b>not</b> for sorting, which is done already.
/// </param>
/// <param name="SharedGenreIds">The shared genres — the concrete reason, which explains the score better than the number.</param>
internal sealed record MatchResponse(Guid UserId, string? DisplayName, string? AvatarUrl, double Score, double Confidence, IReadOnlyList<Guid> SharedGenreIds);
