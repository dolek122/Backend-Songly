using System.Net;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// Reporting posts and the moderation queue, with the default settings. Ported from the report
/// scenarios of the original <c>CommunityModerationIntegrationTest</c>, <c>ReportAbuseIntegrationTest</c>
/// and <c>ReportAccuracyIntegrationTest</c> (without the notification, mail and moderation-log
/// parts, which come with those modules).
/// </summary>
public sealed class ReportFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ReportTestClient _reports = new(factory, factory.CreateClient());

    /// <summary>The full loop: a report reaches the queue and can be dismissed.</summary>
    [Fact]
    public async Task A_report_reaches_the_queue_and_can_be_dismissed()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-reporter");
        var postId = await _reports.Content.PublishedInAsync(author, communityId, "sporny post");

        Assert.Equal(HttpStatusCode.Accepted, (await _reports.ReportAsync(reporter, postId, details: "reklama")).StatusCode);

        var queue = await _reports.QueueAsync(owner, communityId);
        Assert.Equal(1, queue.GetArrayLength());
        var entry = queue[0];
        Assert.Equal("SPAM", entry.GetProperty("reason").GetString());
        Assert.Equal("reklama", entry.GetProperty("details").GetString());
        Assert.Equal(reporter.Id, entry.GetProperty("reporterId").GetGuid());
        Assert.Equal(reporter.User.Email[..reporter.User.Email.IndexOf('@', StringComparison.Ordinal)], entry.GetProperty("reporterDisplayName").GetString());
        Assert.Equal(1, entry.GetProperty("reportCount").GetInt64());
        Assert.Equal(0, entry.GetProperty("reporterUpheldCount").GetInt32());
        Assert.Equal(0, entry.GetProperty("reporterDismissedCount").GetInt32());
        Assert.Equal("sporny post", entry.GetProperty("post").GetProperty("content").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.DismissAsync(owner, communityId, entry.GetProperty("id").GetGuid())).StatusCode);

        Assert.Equal(0, (await _reports.QueueAsync(owner, communityId)).GetArrayLength());
    }

    /// <summary><c>reportCount</c> is how many independent people reported the post, on every entry of it.</summary>
    [Fact]
    public async Task The_queue_counts_independent_reporters_of_a_post()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-count");
        var postId = await _reports.Content.PublishedInAsync(author, communityId);
        for (var i = 0; i < 3; i++)
        {
            await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, $"rep-count-{i}"), postId);
        }

        var queue = await _reports.QueueAsync(owner, communityId);
        Assert.Equal(3, queue.GetArrayLength());
        Assert.All(queue.EnumerateArray(), entry => Assert.Equal(3, entry.GetProperty("reportCount").GetInt64()));
    }

    /// <summary>
    /// One's own post, a second report of the same post, and an ordinary post: an ordinary post has
    /// nobody today with the right to decide a report — moderation is communal, not global.
    /// </summary>
    [Fact]
    public async Task Reporting_rules()
    {
        var (owner, _, communityId) = await _reports.CommunityWithAuthorAsync("rep-rules");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-rules-reporter");
        var postId = await _reports.Content.PublishedInAsync(owner, communityId, "post właściciela");
        var plainPostId = await _reports.Content.PublishAsync(owner, "zwykły post");

        await (await _reports.ReportAsync(owner, postId)).ReadErrorAsync(HttpStatusCode.BadRequest, "CANNOT_REPORT_OWN_POST");
        await _reports.ReportedAsync(reporter, postId);
        await (await _reports.ReportAsync(reporter, postId)).ReadErrorAsync(HttpStatusCode.Conflict, "POST_ALREADY_REPORTED");
        await (await _reports.ReportAsync(reporter, plainPostId)).ReadErrorAsync(HttpStatusCode.BadRequest, "POST_NOT_IN_COMMUNITY");
        await (await _reports.ReportAsync(reporter, Guid.NewGuid())).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    [Fact]
    public async Task A_report_needs_a_known_reason_and_short_details()
    {
        var (_, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-valid");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-valid-reporter");
        var postId = await _reports.Content.PublishedInAsync(author, communityId);

        await (await _client.PostJsonAsync($"/api/v1/posts/{postId}/reports", new { details = "bez powodu" }, reporter.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.BadRequest, (await _reports.ReportAsync(reporter, postId, reason: "NIE_MA_TAKIEGO")).StatusCode);
        await (await _reports.ReportAsync(reporter, postId, details: new string('a', 501))).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.Accepted, (await _reports.ReportAsync(reporter, postId, reason: "HATE_SPEECH", details: new string('a', 500))).StatusCode);
    }

    /// <summary>
    /// A ban cuts off the paths that reach people and leaves the pure numbers (original Claude.md §8).
    /// A report carries an identity and text straight to the moderators, so the banned user loses it
    /// together with writing.
    /// </summary>
    [Fact]
    public async Task A_banned_user_cannot_report()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-ban");
        var troll = await factory.CreateLoggedInUserAsync(_client, "rep-ban-troll");
        await _reports.Communities.JoinedAsync(troll, communityId);
        var postId = await _reports.Content.PublishedInAsync(author, communityId);
        await _reports.Communities.BannedAsync(owner, communityId, troll.Id);

        await (await _reports.ReportAsync(troll, postId)).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");
    }

    /// <summary>A post the reporter may not see does not exist for them — reporting is no way around a block.</summary>
    [Fact]
    public async Task A_post_hidden_by_a_block_cannot_be_reported()
    {
        var (_, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-block");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-block-reporter");
        var postId = await _reports.Content.PublishedInAsync(author, communityId);
        await _reports.Content.BlockAsync(author, reporter);

        await (await _reports.ReportAsync(reporter, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    /// <summary>
    /// The queue is for moderators. The posts in it are shown to them even from someone they blocked:
    /// otherwise a moderator who blocked a troll would stop seeing the troll's reported posts.
    /// </summary>
    [Fact]
    public async Task The_queue_is_for_moderators_and_ignores_their_blocks()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-queue");
        var member = await factory.CreateLoggedInUserAsync(_client, "rep-queue-member");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-queue-reporter");
        await _reports.Communities.JoinedAsync(member, communityId);
        var postId = await _reports.Content.PublishedInAsync(author, communityId);
        await _reports.ReportedAsync(reporter, postId);

        await (await _reports.QueueResponseAsync(member, communityId)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MODERATOR");
        await (await _reports.QueueResponseAsync(owner, Guid.NewGuid())).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
        var reportId = (await _reports.QueueAsync(owner, communityId))[0].GetProperty("id").GetGuid();
        await (await _reports.DismissAsync(member, communityId, reportId)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MODERATOR");

        await _reports.Content.BlockAsync(owner, author);

        var queue = await _reports.QueueAsync(owner, communityId);
        Assert.Equal(postId, queue[0].GetProperty("post").GetProperty("id").GetGuid());
    }

    /// <summary>A report from another community is reported as nonexistent — 403 would confirm the id exists.</summary>
    [Fact]
    public async Task A_report_is_dismissed_only_from_its_own_community()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-other");
        var (otherOwner, _, otherCommunityId) = await _reports.CommunityWithAuthorAsync("rep-other-second");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-other-reporter");
        await _reports.ReportedAsync(reporter, await _reports.Content.PublishedInAsync(author, communityId));
        var reportId = (await _reports.QueueAsync(owner, communityId))[0].GetProperty("id").GetGuid();

        await (await _reports.DismissAsync(otherOwner, otherCommunityId, reportId)).ReadErrorAsync(HttpStatusCode.NotFound, "REPORT_NOT_FOUND");
        await (await _reports.DismissAsync(owner, communityId, Guid.NewGuid())).ReadErrorAsync(HttpStatusCode.NotFound, "REPORT_NOT_FOUND");
        Assert.Equal(1, (await _reports.QueueAsync(owner, communityId)).GetArrayLength());
    }

    /// <summary>Deleting a reported post takes its reports — a report pointing into the void would stay in the queue for ever.</summary>
    [Fact]
    public async Task Deleting_a_reported_post_clears_its_reports()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-del");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-del-reporter");
        var postId = await _reports.Content.PublishedInAsync(author, communityId);
        await _reports.ReportedAsync(reporter, postId);

        await _reports.RemovedAsync(owner, postId);

        Assert.Equal(0, (await _reports.QueueAsync(owner, communityId)).GetArrayLength());
    }

    /// <summary>
    /// The point of original ADR 0027: deleting a post records an upheld report for <b>every</b>
    /// reporter, and the queue shows it next time. Before, the trace went with the post.
    /// </summary>
    [Fact]
    public async Task Removing_a_post_records_an_upheld_report_for_every_reporter()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-upheld");
        var first = await factory.CreateLoggedInUserAsync(_client, "rep-upheld-first");
        var second = await factory.CreateLoggedInUserAsync(_client, "rep-upheld-second");
        var spam = await _reports.Content.PublishedInAsync(author, communityId, "spam do usunięcia");
        await _reports.ReportedAsync(first, spam);
        await _reports.ReportedAsync(second, spam);

        await _reports.RemovedAsync(owner, spam);

        Assert.Equal(1, await _reports.DecisionsAsync(first, "UPHELD"));
        Assert.Equal(1, await _reports.DecisionsAsync(second, "UPHELD"));
        await _reports.ReportedAsync(first, await _reports.Content.PublishedInAsync(author, communityId, "kolejny post"));
        var entry = (await _reports.QueueAsync(owner, communityId))[0];
        Assert.Equal(1, entry.GetProperty("reporterUpheldCount").GetInt32());
        Assert.Equal(0, entry.GetProperty("reporterDismissedCount").GetInt32());
    }

    /// <summary>
    /// Deleting <b>one's own</b> post decides nobody's report — otherwise a spammer would raise the
    /// reporters' accuracy with one click, and it is their decision, not moderation's.
    /// </summary>
    [Fact]
    public async Task An_author_deleting_their_own_post_does_not_count_as_upheld()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-own");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-own-reporter");
        var first = await _reports.Content.PublishedInAsync(author, communityId, "post do samousunięcia");
        await _reports.ReportedAsync(reporter, first);

        await _reports.RemovedAsync(author, first);

        Assert.Equal(0, await _reports.DecisionsAsync(reporter, "UPHELD"));
        await _reports.ReportedAsync(reporter, await _reports.Content.PublishedInAsync(author, communityId, "kolejny post"));
        Assert.Equal(0, (await _reports.QueueAsync(owner, communityId))[0].GetProperty("reporterUpheldCount").GetInt32());
    }

    /// <summary>
    /// One report, one decision. A repeated dismissal (a double click, two moderators at once) used to
    /// record another penalty each time, so one report was enough to push someone's allowance to the floor.
    /// </summary>
    [Fact]
    public async Task Dismissing_the_same_report_twice_records_one_decision()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-twice");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "rep-twice-moderator");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-twice-reporter");
        await _reports.Communities.JoinedAsync(moderator, communityId);
        await _reports.Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR");
        await _reports.ReportedAsync(reporter, await _reports.Content.PublishedInAsync(author, communityId, "post sporny"));
        var reportId = (await _reports.QueueAsync(owner, communityId))[0].GetProperty("id").GetGuid();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await _reports.DismissAsync(owner, communityId, reportId)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.DismissAsync(moderator, communityId, reportId)).StatusCode);
        Assert.Equal(1, await _reports.DecisionsAsync(reporter, "DISMISSED"));
    }

    /// <summary>
    /// Account deletion: the reports a user filed and their track record go; the reports they decided
    /// as a moderator stay and lose only their author — a moderator leaving must not undo decisions.
    /// The community's own decisions go with the community.
    /// </summary>
    [Fact]
    public async Task Deleting_accounts_and_communities_cleans_reports_and_decisions()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("rep-account");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "rep-account-moderator");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "rep-account-reporter");
        await _reports.Communities.JoinedAsync(moderator, communityId);
        await _reports.Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR");
        var dismissedPost = await _reports.Content.PublishedInAsync(author, communityId, "zostaje");
        await _reports.ReportedAsync(reporter, dismissedPost);
        await _reports.DismissAsync(moderator, communityId, (await _reports.QueueAsync(owner, communityId))[0].GetProperty("id").GetGuid());
        var pendingPost = await _reports.Content.PublishedInAsync(author, communityId, "czeka");
        var other = await factory.CreateLoggedInUserAsync(_client, "rep-account-other");
        await _reports.ReportedAsync(other, pendingPost);

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.DeleteAccountAsync(moderator)).StatusCode);
        Assert.Equal(1L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM post_reports WHERE post_id = @post AND status = 'DISMISSED' AND reviewed_by IS NULL", ("post", dismissedPost)));

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.DeleteAccountAsync(other)).StatusCode);
        Assert.Equal(0, (await _reports.QueueAsync(owner, communityId)).GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}", owner.Token)).StatusCode);
        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM reporter_decisions WHERE community_id = @id", ("id", communityId)));
        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.DeleteAccountAsync(reporter)).StatusCode);
    }

    [Fact]
    public async Task Reporting_requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync($"/api/v1/posts/{Guid.NewGuid()}/reports", new { reason = "SPAM" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync($"/api/v1/communities/{Guid.NewGuid()}/post-reports", accessToken: null)).StatusCode);
    }
}
