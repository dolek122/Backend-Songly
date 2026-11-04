using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// The limit per reporter across posts (original ADR 0018), in an instance with a limit of 2. Ported
/// from the original <c>ReportAbuseIntegrationTest</c>; its moderator-mail interval comes with the
/// notification module.
/// </summary>
public sealed class ReportLimitTests : IDisposable
{
    private readonly SonglyApiFactory _factory;
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly ReportTestClient _reports;

    public ReportLimitTests(SonglyApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithSettings(("Songly:Content:Report:Limit", "2"));
        _client = _app.CreateClient();
        _reports = new ReportTestClient(factory, _client);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    /// <summary>The limit counts one person's reports across <b>different</b> posts — exactly what the per-post gates cannot see.</summary>
    [Fact]
    public async Task One_reporter_is_capped_across_different_posts()
    {
        var (_, author, communityId) = await _reports.CommunityWithAuthorAsync("reportlimit-cap");
        var reporter = await _factory.CreateLoggedInUserAsync(_client, "reportlimit-cap-reporter");
        var posts = await _reports.PostsAsync(author, communityId, 3);
        using var rejections = new MetricCollector<long>(_app.Services.GetRequiredService<IMeterFactory>(), "Songly.Content.Moderation", "songly.moderation.report.rejected");

        await _reports.ReportedAsync(reporter, posts[0]);
        await _reports.ReportedAsync(reporter, posts[1]);
        await (await _reports.ReportAsync(reporter, posts[2])).ReadErrorAsync(HttpStatusCode.TooManyRequests, "REPORT_RATE_LIMIT_EXCEEDED");

        // A rejection must leave a trace in the metrics, or flooding the queue is invisible from outside (ADR 0019).
        var rejection = Assert.Single(rejections.GetMeasurementSnapshot());
        Assert.Equal("rate-limit", rejection.Tags["reason"]);

        // The limit is per person: someone else reports normally — their first report, not the third in someone else's window.
        var someoneElse = await _factory.CreateLoggedInUserAsync(_client, "reportlimit-cap-other");
        await _reports.ReportedAsync(someoneElse, posts[2]);
    }

    /// <summary>The window slides: reports older than it stop taking a place.</summary>
    [Fact]
    public async Task Reports_leave_the_window_as_it_slides()
    {
        var (_, author, communityId) = await _reports.CommunityWithAuthorAsync("reportlimit-slide");
        var reporter = await _factory.CreateLoggedInUserAsync(_client, "reportlimit-slide-reporter");
        var posts = await _reports.PostsAsync(author, communityId, 4);
        await _reports.ReportedAsync(reporter, posts[0]);
        await _reports.ReportedAsync(reporter, posts[1]);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _reports.ReportAsync(reporter, posts[2])).StatusCode);

        await _reports.SlideWindowAsync(reporter);

        await _reports.ReportedAsync(reporter, posts[2]);
        await _reports.ReportedAsync(reporter, posts[3]);
    }

    [Fact]
    public async Task A_banned_reporter_is_counted_as_a_rejection_too()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("reportlimit-ban");
        var troll = await _factory.CreateLoggedInUserAsync(_client, "reportlimit-ban-troll");
        var postId = await _reports.Content.PublishedInAsync(author, communityId);
        await _reports.Communities.BannedAsync(owner, communityId, troll.Id);
        using var rejections = new MetricCollector<long>(_app.Services.GetRequiredService<IMeterFactory>(), "Songly.Content.Moderation", "songly.moderation.report.rejected");

        await (await _reports.ReportAsync(troll, postId)).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");

        Assert.Equal("banned", Assert.Single(rejections.GetMeasurementSnapshot()).Tags["reason"]);
    }
}
