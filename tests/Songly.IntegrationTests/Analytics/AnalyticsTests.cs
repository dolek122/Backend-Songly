using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Analytics;

/// <summary>
/// The counters of domain events and the administrators' summary (original ADR 0004, 0019). Ported from the
/// original <c>AnalyticsFlowIntegrationTest</c>, <c>ModerationAnalyticsIntegrationTest</c> and
/// <c>AnalyticsServiceTest</c>.
/// </summary>
/// <remarks>
/// The counters belong to the whole installation, so the original compared deltas before and after — which,
/// with the test classes here running in parallel, would count other tests' likes. Every test runs an instance
/// of its own on a <see cref="TemporaryDatabase"/> instead, and asserts exact numbers.
/// </remarks>
public sealed class AnalyticsTests(SonglyApiFactory factory)
{
    [Fact]
    public async Task The_summary_counts_domain_activity()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var admin = await isolated.AdminAsync("analytics-ada");
        var bono = await isolated.UserAsync("analytics-bono");
        await isolated.UserAsync("analytics-charlie");

        await isolated.Content.BefriendAsync(admin, bono);
        var postId = await isolated.Content.PublishAsync(admin, "analytics test");
        await isolated.Content.LikeAsync(bono, postId);
        await isolated.Content.CommentAsync(bono, postId, "super");
        await isolated.Content.ShareAsync(bono, postId);
        var communityId = await isolated.Communities.CreateAsync(admin, "Analytics fanklub");
        await isolated.Communities.JoinedAsync(bono, communityId);

        var counts = await isolated.CountsAsync(admin);
        Assert.Equal(3, counts["USER_REGISTERED"]);
        Assert.Equal(1, counts["FRIEND_REQUEST_SENT"]);
        Assert.Equal(1, counts["FRIEND_REQUEST_ACCEPTED"]);
        Assert.Equal(1, counts["POST_LIKED"]);
        Assert.Equal(1, counts["POST_COMMENTED"]);
        Assert.Equal(1, counts["POST_SHARED"]);
        Assert.Equal(1, counts["COMMUNITY_MEMBER_JOINED"]);
    }

    /// <summary>A like undone and redone is a like again — the counters stay faithful to the facts; only the inbox deduplicates.</summary>
    [Fact]
    public async Task Every_new_like_counts_even_in_a_loop()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var admin = await isolated.AdminAsync("analytics-loop-admin");
        var fan = await isolated.UserAsync("analytics-loop-fan");
        var postId = await isolated.Content.PublishAsync(admin);

        for (var i = 0; i < 3; i++)
        {
            await isolated.Content.LikeAsync(fan, postId);
            await isolated.Content.UnlikeAsync(fan, postId);
        }

        await isolated.Content.LikeAsync(fan, postId);
        Assert.Equal(4, (await isolated.CountsAsync(admin))["POST_LIKED"]);
    }

    /// <summary>
    /// Reports and their outcomes. <c>POST_FIRST_REPORTED</c> counts <b>posts</b>, not clicks of "report" — a second
    /// report of the same post publishes nothing, so the counter is not the volume of reports by design.
    /// </summary>
    [Fact]
    public async Task Reports_and_their_outcomes_are_counted()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var admin = await isolated.AdminAsync("analytics-rep-admin");
        var (owner, author, communityId) = await isolated.CommunityWithAuthorAsync("analytics-rep");
        var contested = await isolated.Content.PublishedInAsync(author, communityId, "sporny post");
        var removed = await isolated.Content.PublishedInAsync(author, communityId, "post do usuniecia");
        var reporter = await isolated.UserAsync("analytics-rep-reporter");

        await isolated.Reports.ReportedAsync(reporter, contested);
        await isolated.Reports.ReportedAsync(await isolated.UserAsync("analytics-rep-reporter2"), contested);
        await isolated.Reports.ReportedAsync(reporter, removed);
        var firstReport = (await isolated.Reports.QueueAsync(owner, communityId)).EnumerateArray()
            .First(r => r.GetProperty("post").GetProperty("id").GetGuid() == contested).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await isolated.Reports.DismissAsync(owner, communityId, firstReport)).StatusCode);
        await isolated.Reports.RemovedAsync(owner, removed);

        var counts = await isolated.CountsAsync(admin);
        Assert.Equal(2, counts["POST_FIRST_REPORTED"]);
        Assert.Equal(1, counts["POST_REPORT_DISMISSED"]);
        Assert.Equal(1, counts["COMMUNITY_POST_REMOVED"]);
    }

    /// <summary>A comment removed by moderation counts; one deleted by its author does not.</summary>
    [Fact]
    public async Task Only_comments_removed_by_moderation_count()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var admin = await isolated.AdminAsync("analytics-comment-admin");
        var (owner, author, communityId) = await isolated.CommunityWithAuthorAsync("analytics-comment");
        var postId = await isolated.Content.PublishedInAsync(author, communityId);

        await isolated.Content.DeleteCommentAsync(author, postId, await isolated.Content.CommentedAsync(author, postId));
        await isolated.Content.DeleteCommentAsync(owner, postId, await isolated.Content.CommentedAsync(author, postId));

        Assert.Equal(1, (await isolated.CountsAsync(admin))["COMMUNITY_COMMENT_REMOVED"]);
    }

    /// <summary>Bans split by the term — without it nobody could tell whether temporary bans (original ADR 0014) caught on. A repeated ban is no new decision.</summary>
    [Fact]
    public async Task Permanent_and_temporary_bans_are_counted_separately()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var admin = await isolated.AdminAsync("analytics-ban-admin");
        var (owner, firstTroll, communityId) = await isolated.CommunityWithAuthorAsync("analytics-ban");
        var secondTroll = await isolated.UserAsync("analytics-ban-troll2");
        await isolated.Communities.JoinedAsync(secondTroll, communityId);

        await isolated.Communities.BannedAsync(owner, communityId, firstTroll.Id);
        await isolated.Communities.BannedAsync(owner, communityId, secondTroll.Id, durationDays: 7);
        await isolated.Communities.BannedAsync(owner, communityId, secondTroll.Id, durationDays: 7);

        var counts = await isolated.CountsAsync(admin);
        Assert.Equal(1, counts["COMMUNITY_BAN_ISSUED_PERMANENT"]);
        Assert.Equal(1, counts["COMMUNITY_BAN_ISSUED_TEMPORARY"]);
    }

    /// <summary>Appeals submitted, accepted (by unban) and rejected — the series that answer how often the moderation decides itself that it was wrong (original ADR 0017).</summary>
    [Fact]
    public async Task Appeals_and_their_outcomes_are_counted()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var admin = await isolated.AdminAsync("analytics-app-admin");
        var (owner, accepted, communityId) = await isolated.CommunityWithAuthorAsync("analytics-app");
        var rejected = await isolated.UserAsync("analytics-app-rejected");
        await isolated.Communities.JoinedAsync(rejected, communityId);
        await isolated.Communities.BannedAsync(owner, communityId, accepted.Id);
        await isolated.Communities.BannedAsync(owner, communityId, rejected.Id);

        await isolated.Communities.AppealAsync(accepted, communityId);
        await isolated.Communities.AppealAsync(rejected, communityId);
        await isolated.Communities.UnbanAsync(owner, communityId, accepted.Id);
        await isolated.Communities.RejectAppealAsync(owner, communityId, rejected.Id);

        var counts = await isolated.CountsAsync(admin);
        Assert.Equal(2, counts["BAN_APPEAL_SUBMITTED"]);
        Assert.Equal(1, counts["BAN_APPEAL_ACCEPTED"]);
        Assert.Equal(1, counts["BAN_APPEAL_REJECTED"]);
    }

    /// <summary>
    /// Every type is in the summary, a zero included, in a window clamped to a day…a year; what happened before the
    /// window is not counted.
    /// </summary>
    [Fact]
    public async Task The_window_is_clamped_and_every_type_is_listed()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var admin = await isolated.AdminAsync("analytics-window-admin");
        await isolated.Database.ExecuteSqlAsync("INSERT INTO analytics_events (id, type, occurred_at) VALUES (gen_random_uuid(), 'POST_SHARED', now() - interval '10 days')");

        var week = await isolated.SummaryAsync(admin, "?days=7");
        Assert.Equal(7, week.GetProperty("windowDays").GetInt32());
        Assert.Equal(16, week.GetProperty("counts").GetArrayLength());
        Assert.Equal(0, Count(week, "POST_SHARED"));
        Assert.Equal(1, Count(await isolated.SummaryAsync(admin, "?days=30"), "POST_SHARED"));

        Assert.Equal(7, (await isolated.SummaryAsync(admin, string.Empty)).GetProperty("windowDays").GetInt32());
        Assert.Equal(1, (await isolated.SummaryAsync(admin, "?days=0")).GetProperty("windowDays").GetInt32());
        Assert.Equal(1, (await isolated.SummaryAsync(admin, "?days=-5")).GetProperty("windowDays").GetInt32());
        Assert.Equal(365, (await isolated.SummaryAsync(admin, "?days=10000")).GetProperty("windowDays").GetInt32());
    }

    /// <summary>The same event also feeds the observability metric, tagged by type.</summary>
    [Fact]
    public async Task Every_counted_event_is_a_metric_too()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var author = await isolated.UserAsync("analytics-metric-author");
        var fan = await isolated.UserAsync("analytics-metric-fan");
        using var metric = new MetricCollector<long>(isolated.App.Services.GetRequiredService<IMeterFactory>(), "Songly.Analytics", "songly.domain.events");

        await isolated.Content.LikeAsync(fan, await isolated.Content.PublishAsync(author));

        Assert.Equal("POST_LIKED", Assert.Single(metric.GetMeasurementSnapshot()).Tags["type"]);
    }

    /// <summary>A count is a reaction to a committed fact: when it cannot be written, the like still stands and the request succeeds.</summary>
    [Fact]
    public async Task A_like_stands_when_it_cannot_be_counted()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var author = await isolated.UserAsync("analytics-failure-author");
        var fan = await isolated.UserAsync("analytics-failure-fan");
        var postId = await isolated.Content.PublishAsync(author);
        await isolated.Database.ExecuteSqlAsync("ALTER TABLE analytics_events RENAME TO analytics_events_gone");

        Assert.Equal(HttpStatusCode.NoContent, (await isolated.Content.LikeAsync(fan, postId)).StatusCode);
        Assert.Equal(1, (await isolated.Content.PostAsync(author, postId)).GetProperty("likeCount").GetInt64());
    }

    [Fact]
    public async Task The_summary_is_for_administrators_only()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/analytics/summary", accessToken: null)).StatusCode);

        var user = await factory.CreateLoggedInUserAsync(client, "analytics-non-admin");
        await (await client.GetAsync("/api/v1/analytics/summary", user.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    private static long Count(JsonElement summary, string type) =>
        summary.GetProperty("counts").EnumerateArray().Single(c => c.GetProperty("type").GetString() == type).GetProperty("count").GetInt64();

    /// <summary>An instance on a database of its own: the counters start at zero.</summary>
    private sealed class Isolated : IAsyncDisposable
    {
        private readonly HttpClient _client;

        private Isolated(TemporaryDatabase database, WebApplicationFactory<Program> app, SonglyApiFactory factory)
        {
            Database = database;
            App = app;
            _client = app.CreateClient();
            Content = new ContentTestClient(_client);
            Communities = new CommunityTestClient(factory, _client);
            Reports = new ReportTestClient(factory, _client);
        }

        public TemporaryDatabase Database { get; }

        public WebApplicationFactory<Program> App { get; }

        public ContentTestClient Content { get; }

        public CommunityTestClient Communities { get; }

        public ReportTestClient Reports { get; }

        public static async Task<Isolated> StartAsync(SonglyApiFactory factory)
        {
            var database = await TemporaryDatabase.CreateAsync(factory);
            return new Isolated(database, factory.WithSettings(database.Settings), factory);
        }

        public async Task<LoggedInUser> UserAsync(string prefix)
        {
            var email = TestAccounts.UniqueEmail(prefix);
            var tokens = await _client.RegisterAndVerifyAsync(App.MailsOf(), email);
            return new LoggedInUser(new TestUser(TestTokens.SubjectOf(tokens.AccessToken), email, TestAccounts.DefaultPassword), tokens);
        }

        /// <summary>The role is read live on every request, so the token from before the update works as an admin's.</summary>
        public async Task<LoggedInUser> AdminAsync(string prefix)
        {
            var admin = await UserAsync(prefix);
            await Database.ExecuteSqlAsync("UPDATE users SET is_admin = TRUE WHERE id = @id", ("id", admin.Id));
            return admin;
        }

        public async Task<(LoggedInUser Owner, LoggedInUser Member, Guid CommunityId)> CommunityWithAuthorAsync(string prefix)
        {
            var owner = await UserAsync($"{prefix}-owner");
            var member = await UserAsync($"{prefix}-member");
            var communityId = await Communities.CreateAsync(owner, prefix);
            await Communities.JoinedAsync(member, communityId);
            return (owner, member, communityId);
        }

        public async Task<JsonElement> SummaryAsync(LoggedInUser admin, string query)
        {
            using var response = await _client.GetAsync($"/api/v1/analytics/summary{query}", admin.Token);
            return await response.ReadJsonAsync();
        }

        public async Task<IReadOnlyDictionary<string, long>> CountsAsync(LoggedInUser admin) =>
            (await SummaryAsync(admin, string.Empty)).GetProperty("counts").EnumerateArray()
                .ToDictionary(c => c.GetProperty("type").GetString()!, c => c.GetProperty("count").GetInt64());

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await App.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
