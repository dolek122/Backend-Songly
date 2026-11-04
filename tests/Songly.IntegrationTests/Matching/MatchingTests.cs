using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Matching;

/// <summary>
/// <c>GET /api/v1/matches</c> through the whole application. Ported from the original
/// <c>MatchingFlowIntegrationTest</c>, plus the candidate rules (blocks, suspensions) and the paging
/// that the original tested on mocks — here they are the service's, and only the API reaches them.
/// </summary>
/// <remarks>
/// <para>
/// The database is shared, so these tests use the genres at the end of the V2 seed, which no other
/// test touches, and find candidates by id instead of by position.
/// </para>
/// <para>
/// Since the original ADR 0029 a score depends on the popularity of genres across the <b>whole</b>
/// database, so no assertion here pins an exact number — the arithmetic belongs to
/// <c>MatchRankingTests</c>; here we check the order, and that the weights really come through SQL.
/// The popularity snapshot (ADR 0032) is not a risk: every change of genres here evicts it after its
/// commit, and only this class (its tests one after another) asks for it in the shared instance.
/// </para>
/// </remarks>
public sealed class MatchingTests(SonglyApiFactory factory)
{
    private static readonly Guid Reggae = Guid.Parse("a5a9f290-1111-4a2e-8b1a-00000000000b");
    private static readonly Guid Latin = Guid.Parse("a5a9f290-1111-4a2e-8b1a-00000000000c");
    private static readonly Guid Kpop = Guid.Parse("a5a9f290-1111-4a2e-8b1a-00000000000d");
    private static readonly Guid Folk = Guid.Parse("a5a9f290-1111-4a2e-8b1a-00000000000e");
    private static readonly Guid Punk = Guid.Parse("a5a9f290-1111-4a2e-8b1a-00000000000f");

    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _social = new(factory.CreateClient());

    /// <summary>
    /// The shape of the answer: who shares which genres, with their display data, and a score inside
    /// (0, 1) — since the original ADR 0033, 1.0 cannot be reached.
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> asserted: that more shared genres rank higher. Since ADR 0029 that is no
    /// longer a property of the algorithm — two shared mainstream genres can lose to one niche genre,
    /// which was the point. The order is the next test's, which sets the popularity itself.
    /// </remarks>
    [Fact]
    public async Task Matches_report_the_shared_genres()
    {
        var ada = await UserAsync("match-ada");
        var bono = await UserAsync("match-bono");
        var carol = await UserAsync("match-carol");
        await SetGenresAsync(ada, Reggae, Latin, Kpop);
        await SetGenresAsync(bono, Reggae, Latin, Folk);
        await SetGenresAsync(carol, Kpop);

        var matches = await MatchesAsync(ada);

        Assert.DoesNotContain(ada.Id, UserIds(matches));
        var bonoMatch = MatchOf(matches, bono);
        Assert.Equal(new[] { Reggae, Latin }.Order(), SharedGenres(bonoMatch).Order());
        Assert.Equal(NameOf(bono), bonoMatch.GetProperty("displayName").GetString());
        Assert.Equal([Kpop], SharedGenres(MatchOf(matches, carol)));
        Assert.InRange(MatchOf(matches, bono).GetProperty("score").GetDouble(), double.Epsilon, 1.0 - 1e-9);
        Assert.InRange(MatchOf(matches, carol).GetProperty("score").GetDouble(), double.Epsilon, 1.0 - 1e-9);
    }

    /// <summary>
    /// The point of the original ADR 0029 through real queries: both candidates share <b>exactly one</b>
    /// genre with the viewer and have identical unions (the same third genre), so a raw Jaccard tied
    /// them at 1/3 and the id decided. The rarity weight puts the one we share a niche with higher.
    /// </summary>
    [Fact]
    public async Task A_shared_rare_genre_ranks_above_a_shared_mainstream_one()
    {
        var viewer = await UserAsync("wmatch-viewer");
        var nicheTwin = await UserAsync("wmatch-niche");
        var mainstreamTwin = await UserAsync("wmatch-mainstream");

        // Reggae becomes mainstream in this database; folk stays a niche.
        for (var i = 0; i < 6; i++)
        {
            await SetGenresAsync(await UserAsync($"wmatch-crowd-{i}"), Reggae);
        }

        await SetGenresAsync(viewer, Folk, Reggae);
        await SetGenresAsync(nicheTwin, Folk, Latin);
        await SetGenresAsync(mainstreamTwin, Reggae, Latin);

        var matches = await MatchesAsync(viewer);

        Assert.True(ScoreOf(matches, nicheTwin) > ScoreOf(matches, mainstreamTwin));
        var ranking = UserIds(matches);
        Assert.True(ranking.IndexOf(nicheTwin.Id) < ranking.IndexOf(mainstreamTwin.Id));
    }

    /// <summary>
    /// The point of the original ADR 0033 through the whole API: two pairs with <b>identical</b> taste,
    /// both fully covered — yet the pair that told more about itself scores higher. Before, both scored
    /// 1.0, because the proportion covered does not know how much data stands behind it.
    /// </summary>
    /// <remarks>
    /// Robust to the shared database: the rich pair's union is a superset of the poor pair's, and the
    /// weights are positive, so the inequality holds whatever the popularity.
    /// </remarks>
    [Fact]
    public async Task The_score_grows_with_how_much_both_profiles_tell()
    {
        var poorViewer = await UserAsync("conf-poor-viewer");
        var poorTwin = await UserAsync("conf-poor-twin");
        var richViewer = await UserAsync("conf-rich-viewer");
        var richTwin = await UserAsync("conf-rich-twin");
        await SetGenresAsync(poorViewer, Reggae);
        await SetGenresAsync(poorTwin, Reggae);
        Guid[] everything = [Reggae, Latin, Kpop, Folk, Punk];
        await SetGenresAsync(richViewer, everything);
        await SetGenresAsync(richTwin, everything);

        var poor = MatchOf(await MatchesAsync(poorViewer), poorTwin);
        var rich = MatchOf(await MatchesAsync(richViewer), richTwin);

        Assert.True(rich.GetProperty("score").GetDouble() > poor.GetProperty("score").GetDouble());

        // Nobody gets 1.0 any more, on purpose: nobody's taste is known completely.
        Assert.True(rich.GetProperty("score").GetDouble() < 1.0);

        // The confidence travels next to the score, so a client can avoid a percentage nothing backs.
        Assert.InRange(poor.GetProperty("confidence").GetDouble(), double.Epsilon, 1.0 - 1e-9);
        Assert.True(rich.GetProperty("confidence").GetDouble() > poor.GetProperty("confidence").GetDouble());
    }

    [Fact]
    public async Task Friends_are_not_proposed()
    {
        var ada = await UserAsync("match-friend-ada");
        var bono = await UserAsync("match-friend-bono");
        await SetGenresAsync(ada, Punk);
        await SetGenresAsync(bono, Punk);
        Assert.Contains(bono.Id, UserIds(await MatchesAsync(ada)));

        await _social.BefriendAsync(ada, bono);

        Assert.DoesNotContain(bono.Id, UserIds(await MatchesAsync(ada)));
    }

    /// <summary>A block works both ways here too: neither side is proposed to the other.</summary>
    [Fact]
    public async Task Blocked_users_are_not_proposed_either_way()
    {
        var blocker = await UserAsync("match-blocker");
        var blocked = await UserAsync("match-blocked");
        await SetGenresAsync(blocker, Punk);
        await SetGenresAsync(blocked, Punk);
        Assert.Contains(blocked.Id, UserIds(await MatchesAsync(blocker)));

        await _social.BlockAsync(blocker, blocked);

        Assert.DoesNotContain(blocked.Id, UserIds(await MatchesAsync(blocker)));
        Assert.DoesNotContain(blocker.Id, UserIds(await MatchesAsync(blocked)));
    }

    /// <summary>
    /// A suspended account is not proposed (original ADR 0029): a match is an invitation to get in
    /// touch, and suspensions usually come from harassment.
    /// </summary>
    [Fact]
    public async Task Suspended_accounts_are_not_proposed()
    {
        var viewer = await UserAsync("match-suspension-viewer");
        var suspended = await UserAsync("match-suspended");
        await SetGenresAsync(viewer, Punk);
        await SetGenresAsync(suspended, Punk);
        Assert.Contains(suspended.Id, UserIds(await MatchesAsync(viewer)));

        await factory.SuspendAsync(suspended.User);

        Assert.DoesNotContain(suspended.Id, UserIds(await MatchesAsync(viewer)));
    }

    [Fact]
    public async Task Someone_sharing_no_genre_is_not_proposed()
    {
        var viewer = await UserAsync("match-nothing-shared");
        var other = await UserAsync("match-nothing-shared-other");
        await SetGenresAsync(viewer, Kpop);
        await SetGenresAsync(other, Folk);

        Assert.DoesNotContain(other.Id, UserIds(await MatchesAsync(viewer)));
    }

    /// <summary>
    /// The ranking is cut into pages in memory, after the scores: the same order on every page, nobody
    /// twice, and the total counts every candidate — also on a page past the end.
    /// </summary>
    [Fact]
    public async Task Matches_are_paged_in_the_ranked_order()
    {
        var viewer = await UserAsync("match-paging");
        await SetGenresAsync(viewer, Punk, Folk, Kpop);
        for (var i = 0; i < 3; i++)
        {
            await SetGenresAsync(await UserAsync($"match-paging-{i}"), Punk, Folk, Kpop);
        }

        var all = await PageAsync(viewer, "?size=100");
        var total = all.GetProperty("page").GetProperty("totalElements").GetInt64();
        Assert.True(total >= 3);

        var first = await PageAsync(viewer, "?size=2&page=0");
        var second = await PageAsync(viewer, "?size=2&page=1");
        var expected = UserIds(all.GetProperty("content")).Take(4).ToList();
        Assert.Equal(expected, [.. UserIds(first.GetProperty("content")), .. UserIds(second.GetProperty("content"))]);
        Assert.Equal(total, second.GetProperty("page").GetProperty("totalElements").GetInt64());

        var pastTheEnd = await PageAsync(viewer, "?size=100&page=50");
        Assert.Equal(0, pastTheEnd.GetProperty("content").GetArrayLength());
        Assert.Equal(total, pastTheEnd.GetProperty("page").GetProperty("totalElements").GetInt64());
    }

    [Fact]
    public async Task Matches_are_empty_without_favourite_genres()
    {
        var viewer = await UserAsync("match-nogenres");

        var page = await PageAsync(viewer, string.Empty);

        Assert.Equal(0, page.GetProperty("content").GetArrayLength());
        Assert.Equal(0, page.GetProperty("page").GetProperty("totalElements").GetInt64());
    }

    [Fact]
    public async Task Matches_require_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/matches", accessToken: null)).StatusCode);
    }

    private Task<LoggedInUser> UserAsync(string prefix) => factory.CreateLoggedInUserAsync(_client, prefix);

    private async Task SetGenresAsync(LoggedInUser user, params Guid[] genreIds) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds }, user.Token)).StatusCode);

    private async Task<JsonElement> PageAsync(LoggedInUser viewer, string query)
    {
        using var response = await _client.GetAsync($"/api/v1/matches{query}", viewer.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private async Task<JsonElement> MatchesAsync(LoggedInUser viewer) => (await PageAsync(viewer, "?size=100")).GetProperty("content");

    private static List<Guid> UserIds(JsonElement matches) => [.. matches.EnumerateArray().Select(match => match.GetProperty("userId").GetGuid())];

    private static JsonElement MatchOf(JsonElement matches, LoggedInUser candidate) =>
        matches.EnumerateArray().SingleOrDefault(match => match.GetProperty("userId").GetGuid() == candidate.Id) is { ValueKind: JsonValueKind.Object } match
            ? match
            : throw new Xunit.Sdk.XunitException($"No match for {candidate.Id}");

    private static double ScoreOf(JsonElement matches, LoggedInUser candidate) => MatchOf(matches, candidate).GetProperty("score").GetDouble();

    private static Guid[] SharedGenres(JsonElement match) => [.. match.GetProperty("sharedGenreIds").EnumerateArray().Select(id => id.GetGuid())];

    private static string NameOf(LoggedInUser user) => user.User.Email[..user.User.Email.IndexOf('@', StringComparison.Ordinal)];
}
