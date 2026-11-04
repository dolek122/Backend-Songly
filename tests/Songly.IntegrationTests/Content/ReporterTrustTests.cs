using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// The allowance depends on the reporter's history (original ADR 0025, 0027, 0030), in an instance
/// with a limit of 4, a penalty of 2 per net dismissal, a floor of 1 and a history window of an hour.
/// Ported from the original <c>ReporterTrustIntegrationTest</c> and the allowance scenarios of
/// <c>ReportAccuracyIntegrationTest</c>.
/// </summary>
/// <remarks>
/// Two windows, moved separately: <see cref="ReportTestClient.SlideWindowAsync"/> takes reports out of
/// the hour's count (intensity), <see cref="ReportTestClient.AgeDecisionsAsync"/> takes decisions out of
/// the history (memory). A dismissed report still takes its place in the window, so the lowered
/// allowance shows only after the window moved — otherwise the refusal would come from the reports
/// sitting in this hour and prove nothing about the penalty.
/// </remarks>
public sealed class ReporterTrustTests : IDisposable
{
    private readonly SonglyApiFactory _factory;
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly ReportTestClient _reports;

    public ReporterTrustTests(SonglyApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithSettings(
            ("Songly:Content:Report:Limit", "4"),
            ("Songly:Content:Report:DismissalPenalty", "2"),
            ("Songly:Content:Report:MinLimit", "1"),
            ("Songly:Content:Report:HistoryWindow", "01:00:00"));
        _client = _app.CreateClient();
        _reports = new ReportTestClient(factory, _client);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    /// <summary>
    /// The point of ADR 0025: dismissals eat the allowance. Two dismissals at a penalty of 2 leave one
    /// report per window — and an ordinary user's allowance is untouched.
    /// </summary>
    [Fact]
    public async Task Dismissals_shrink_the_reporting_allowance()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("trust-shrink");
        var troll = await _factory.CreateLoggedInUserAsync(_client, "trust-shrink-troll");
        var honest = await _factory.CreateLoggedInUserAsync(_client, "trust-shrink-honest");
        var posts = await _reports.PostsAsync(author, communityId, 6);
        await _reports.ReportedAsync(troll, posts[0]);
        await _reports.ReportedAsync(troll, posts[1]);
        await _reports.DismissAllAsync(owner, communityId);
        await _reports.SlideWindowAsync(troll);

        // One still goes through (the floor), the next does not.
        await _reports.ReportedAsync(troll, posts[2]);
        await (await _reports.ReportAsync(troll, posts[3])).ReadErrorAsync(HttpStatusCode.TooManyRequests, "REPORT_RATE_LIMIT_EXCEEDED");

        // Someone without dismissals has the full allowance: four reports go through.
        for (var i = 0; i < 4; i++)
        {
            await _reports.ReportedAsync(honest, posts[i]);
        }
    }

    /// <summary>The floor never reaches zero — or a few dismissals would take away the tool one could report the moderator's own abuse with.</summary>
    [Fact]
    public async Task Reporting_is_never_fully_silenced()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("trust-floor");
        var troll = await _factory.CreateLoggedInUserAsync(_client, "trust-floor-troll");
        var posts = await _reports.PostsAsync(author, communityId, 5);

        // Four dismissals are a penalty of 8 at a limit of 4 — the allowance would go negative.
        for (var i = 0; i < 4; i++)
        {
            await _reports.ReportedAsync(troll, posts[i]);
            await _reports.DismissAllAsync(owner, communityId);
            await _reports.SlideWindowAsync(troll);
        }

        await _reports.ReportedAsync(troll, posts[4]);
    }

    /// <summary>The other half of ADR 0027: upheld reports forgive dismissals one for one.</summary>
    [Fact]
    public async Task Upheld_reports_offset_dismissals()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("trust-offset");
        var reporter = await _factory.CreateLoggedInUserAsync(_client, "trust-offset-reporter");
        var posts = await _reports.PostsAsync(author, communityId, 6);

        // Two dismissals would take the allowance from 4 to the floor (2 × 2)...
        await _reports.ReportedAsync(reporter, posts[0]);
        await _reports.ReportedAsync(reporter, posts[1]);
        await _reports.DismissAllAsync(owner, communityId);
        await _reports.SlideWindowAsync(reporter);

        // ...but one upheld report takes one dismissal back.
        await _reports.ReportedAsync(reporter, posts[2]);
        await _reports.RemovedAsync(owner, posts[2]);
        await _reports.SlideWindowAsync(reporter);

        // Net one dismissal: 4 - 2 = 2 — two go through, the third does not.
        await _reports.ReportedAsync(reporter, posts[3]);
        await _reports.ReportedAsync(reporter, posts[4]);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _reports.ReportAsync(reporter, posts[5])).StatusCode);
    }

    /// <summary>Upheld reports never raise the allowance <b>above</b> the base — that would open a wider channel for flooding.</summary>
    [Fact]
    public async Task Upheld_reports_never_raise_the_allowance_above_the_base()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("trust-cap");
        var reporter = await _factory.CreateLoggedInUserAsync(_client, "trust-cap-reporter");
        var posts = await _reports.PostsAsync(author, communityId, 8);
        for (var i = 0; i < 3; i++)
        {
            await _reports.ReportedAsync(reporter, posts[i]);
            await _reports.RemovedAsync(owner, posts[i]);
        }

        await _reports.SlideWindowAsync(reporter);

        for (var i = 3; i < 7; i++)
        {
            await _reports.ReportedAsync(reporter, posts[i]);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await _reports.ReportAsync(reporter, posts[7])).StatusCode);
    }

    /// <summary>The queue shows the moderator the reporter's history — a weight to judge, not subtracted for them.</summary>
    [Fact]
    public async Task The_queue_shows_the_reporters_dismissal_count()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("trust-queue");
        var troll = await _factory.CreateLoggedInUserAsync(_client, "trust-queue-troll");
        var posts = await _reports.PostsAsync(author, communityId, 2);
        await _reports.ReportedAsync(troll, posts[0]);
        await _reports.DismissAllAsync(owner, communityId);

        await _reports.ReportedAsync(troll, posts[1]);

        var entry = (await _reports.QueueAsync(owner, communityId))[0];
        Assert.Equal(troll.Id, entry.GetProperty("reporterId").GetGuid());
        Assert.Equal(1, entry.GetProperty("reporterDismissedCount").GetInt32());
    }

    /// <summary>
    /// The scopes differ <b>on purpose</b>: a dismissal in one community eats the allowance globally
    /// (flooding spread over ten communities would slip past a limit per community), but the moderator
    /// of another community does not see it — moderation is communal, not global.
    /// </summary>
    [Fact]
    public async Task Dismissals_count_globally_for_the_limit_but_locally_for_the_queue()
    {
        var (firstOwner, author, firstCommunity) = await _reports.CommunityWithAuthorAsync("trust-scope-first");
        var secondOwner = await _factory.CreateLoggedInUserAsync(_client, "trust-scope-second-owner");
        var secondCommunity = await _reports.Communities.CreateAsync(secondOwner, "trust-scope-second");
        await _reports.Communities.JoinedAsync(author, secondCommunity);
        var troll = await _factory.CreateLoggedInUserAsync(_client, "trust-scope-troll");
        var inFirst = await _reports.PostsAsync(author, firstCommunity, 2);
        var inSecond = await _reports.PostsAsync(author, secondCommunity, 2);
        await _reports.ReportedAsync(troll, inFirst[0]);
        await _reports.DismissAllAsync(firstOwner, firstCommunity);
        await _reports.ReportedAsync(troll, inFirst[1]);
        await _reports.DismissAllAsync(firstOwner, firstCommunity);
        await _reports.SlideWindowAsync(troll);

        // The allowance is eaten in the second community too: one goes through (the floor), the next does not.
        await _reports.ReportedAsync(troll, inSecond[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _reports.ReportAsync(troll, inSecond[1])).StatusCode);

        // ...but the second community's moderator does not see those dismissals.
        var entry = (await _reports.QueueAsync(secondOwner, secondCommunity))[0];
        Assert.Equal(troll.Id, entry.GetProperty("reporterId").GetGuid());
        Assert.Equal(0, entry.GetProperty("reporterDismissedCount").GetInt32());
    }

    /// <summary>
    /// The point of ADR 0030: the penalty lapses. Dismissals older than the history window stop eating
    /// the allowance — and nothing has to remember that, because nothing wakes up at expiry.
    /// </summary>
    [Fact]
    public async Task Dismissals_stop_counting_once_they_leave_the_history_window()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("trust-decay");
        var troll = await _factory.CreateLoggedInUserAsync(_client, "trust-decay-troll");
        var posts = await _reports.PostsAsync(author, communityId, 8);
        await _reports.ReportedAsync(troll, posts[0]);
        await _reports.ReportedAsync(troll, posts[1]);
        await _reports.DismissAllAsync(owner, communityId);
        await _reports.SlideWindowAsync(troll);
        await _reports.ReportedAsync(troll, posts[2]);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _reports.ReportAsync(troll, posts[3])).StatusCode);

        await _reports.AgeDecisionsAsync(troll);
        await _reports.SlideWindowAsync(troll);

        for (var i = 3; i < 7; i++)
        {
            await _reports.ReportedAsync(troll, posts[i]);
        }

        // Back to the base, not to infinity: the fifth in this window does not go through.
        await (await _reports.ReportAsync(troll, posts[7])).ReadErrorAsync(HttpStatusCode.TooManyRequests, "REPORT_RATE_LIMIT_EXCEEDED");
    }

    /// <summary>
    /// The queue counts in the same window as the allowance (ADR 0030) — a number without a time scale
    /// is not a weight a person can judge. The report itself stays: the reporter's history ages, not
    /// the case to decide.
    /// </summary>
    [Fact]
    public async Task The_queue_forgets_dismissals_older_than_the_window()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("trust-decayqueue");
        var troll = await _factory.CreateLoggedInUserAsync(_client, "trust-decayqueue-troll");
        var posts = await _reports.PostsAsync(author, communityId, 2);
        await _reports.ReportedAsync(troll, posts[0]);
        await _reports.DismissAllAsync(owner, communityId);
        await _reports.ReportedAsync(troll, posts[1]);
        Assert.Equal(1, (await _reports.QueueAsync(owner, communityId))[0].GetProperty("reporterDismissedCount").GetInt32());

        await _reports.AgeDecisionsAsync(troll);

        var entry = (await _reports.QueueAsync(owner, communityId))[0];
        Assert.Equal(troll.Id, entry.GetProperty("reporterId").GetGuid());
        Assert.Equal(0, entry.GetProperty("reporterDismissedCount").GetInt32());
    }

    /// <summary>A zero floor refuses to start: it would let a few dismissals silence someone for good.</summary>
    [Fact]
    public void A_floor_of_zero_refuses_to_start()
    {
        using var app = _factory.WithSettings(("Songly:Content:Report:MinLimit", "0"));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }
}
