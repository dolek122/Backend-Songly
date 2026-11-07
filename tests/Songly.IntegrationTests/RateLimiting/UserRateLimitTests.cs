using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Songly.IntegrationTests.Content;

namespace Songly.IntegrationTests.RateLimiting;

/// <summary>
/// The limit per user (original ADR 0034) on small numbers: a write budget of 6, an interaction budget of 2.
/// Ported from the original <c>UserRateLimitingIntegrationTest</c> and <c>UserRateLimitingFilterTest</c>. Every
/// test makes its own accounts, so the windows (keyed by the account) do not leak between tests.
/// </summary>
/// <remarks>
/// The accounts are created through the shared instance, where the limit is off — so the setup's own writes
/// (the display name) do not take from the budget the test counts.
/// </remarks>
public sealed class UserRateLimitTests : IDisposable
{
    private readonly SonglyApiFactory _factory;
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly ContentTestClient _content;

    public UserRateLimitTests(SonglyApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithSettings(
            ("Songly:Security:UserRateLimit:Enabled", "true"),
            ("Songly:Security:UserRateLimit:Rules:0:Name", "writes"),
            ("Songly:Security:UserRateLimit:Rules:0:Paths:0", "/api/**"),
            ("Songly:Security:UserRateLimit:Rules:0:Limit", "6"),
            ("Songly:Security:UserRateLimit:Rules:0:Window", "00:01:00"),
            ("Songly:Security:UserRateLimit:Rules:1:Name", "interactions"),
            ("Songly:Security:UserRateLimit:Rules:1:Methods:0", "PUT"),
            ("Songly:Security:UserRateLimit:Rules:1:Methods:1", "POST"),
            ("Songly:Security:UserRateLimit:Rules:1:Paths:0", "/api/v1/posts/*/likes"),
            ("Songly:Security:UserRateLimit:Rules:1:Paths:1", "/api/v1/posts/*/comments"),
            ("Songly:Security:UserRateLimit:Rules:1:Limit", "2"),
            ("Songly:Security:UserRateLimit:Rules:1:Window", "00:10:00"));
        _client = _app.CreateClient();
        _content = new ContentTestClient(_client);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    /// <summary>
    /// A like and a comment share one budget — otherwise spreading the flood over two endpoints would double the
    /// limit, and for the victim it ends the same way: in a notification.
    /// </summary>
    [Fact]
    public async Task Interactions_share_one_budget_across_endpoints()
    {
        var (spammer, postId) = await SpammerAndPostAsync("userlimit-shared");
        using var rejections = new MetricCollector<long>(_app.Services.GetRequiredService<IMeterFactory>(), "Songly.RateLimiting", "songly.rate.limit.user.rejected");

        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(spammer, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _content.CommentAsync(spammer, postId)).StatusCode);

        var refused = await _content.CommentAsync(spammer, postId);

        await refused.ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
        Assert.InRange(int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture), 1, 600);
        Assert.Equal("interactions", Assert.Single(rejections.GetMeasurementSnapshot()).Tags["rule"]);
    }

    /// <summary>The window belongs to the person — not to the post, and not to the address (the test server has one).</summary>
    [Fact]
    public async Task The_limit_is_per_user()
    {
        var (spammer, postId) = await SpammerAndPostAsync("userlimit-per-user");
        var bystander = await _factory.CreateLoggedInUserAsync(_client, "userlimit-per-user-bystander");

        await _content.CommentAsync(spammer, postId);
        await _content.CommentAsync(spammer, postId);
        await (await _content.CommentAsync(spammer, postId)).ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");

        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(bystander, postId)).StatusCode);
    }

    /// <summary>
    /// Withdrawing one's own trace is not an interaction and must not depend on the limit — the same principle by
    /// which withdrawing a like does not check a block. Reads are not limited at all.
    /// </summary>
    [Fact]
    public async Task Withdrawing_and_reading_still_work_once_the_interaction_budget_is_spent()
    {
        var (spammer, postId) = await SpammerAndPostAsync("userlimit-withdraw");
        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(spammer, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _content.CommentAsync(spammer, postId)).StatusCode);
        await (await _content.CommentAsync(spammer, postId)).ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");

        Assert.Equal(HttpStatusCode.NoContent, (await _content.UnlikeAsync(spammer, postId)).StatusCode);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await _content.GetPostAsync(spammer, postId)).StatusCode);
        }
    }

    /// <summary>
    /// The write budget covers the whole prefix, so an endpoint no rule names is limited all the same — a new
    /// endpoint is protected by default.
    /// </summary>
    [Fact]
    public async Task The_write_budget_covers_endpoints_no_rule_names()
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, "userlimit-writes");

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/v1/notifications/read-all", new { }, user.Token)).StatusCode);
        }

        await (await _client.PostJsonAsync("/api/v1/notifications/read-all", new { }, user.Token)).ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
    }

    /// <summary>
    /// With two rules exceeded, Retry-After points at the later window — a shorter one would promise a return
    /// that ends in another 429.
    /// </summary>
    [Fact]
    public async Task With_two_rules_exceeded_retry_after_points_at_the_later_window()
    {
        using var app = _factory.WithSettings(
            ("Songly:Security:UserRateLimit:Enabled", "true"),
            ("Songly:Security:UserRateLimit:Rules:0:Name", "writes"),
            ("Songly:Security:UserRateLimit:Rules:0:Paths:0", "/api/**"),
            ("Songly:Security:UserRateLimit:Rules:0:Limit", "1"),
            ("Songly:Security:UserRateLimit:Rules:0:Window", "00:01:00"),
            ("Songly:Security:UserRateLimit:Rules:1:Name", "interactions"),
            ("Songly:Security:UserRateLimit:Rules:1:Paths:0", "/api/v1/posts/*/likes"),
            ("Songly:Security:UserRateLimit:Rules:1:Limit", "1"),
            ("Songly:Security:UserRateLimit:Rules:1:Window", "00:10:00"));
        using var client = app.CreateClient();
        var content = new ContentTestClient(client);
        var (spammer, postId) = await SpammerAndPostAsync("userlimit-later");
        Assert.Equal(HttpStatusCode.NoContent, (await content.LikeAsync(spammer, postId)).StatusCode);

        var refused = await content.LikeAsync(spammer, postId);

        await refused.ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
        Assert.InRange(int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")), CultureInfo.InvariantCulture), 61, 600);
    }

    /// <summary>
    /// The default rules apply when none are configured — the limit is on by default with the original's budgets,
    /// not a set of empty rules.
    /// </summary>
    [Fact]
    public async Task The_default_rules_apply_when_none_are_configured()
    {
        using var app = _factory.WithSettings(("Songly:Security:UserRateLimit:Enabled", "true"));
        using var client = app.CreateClient();
        var content = new ContentTestClient(client);
        var (spammer, postId) = await SpammerAndPostAsync("userlimit-defaults");

        // 40 interactions in ten minutes: a like is idempotent, but every PUT is still a reach towards someone.
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await content.LikeAsync(spammer, postId)).StatusCode);
        }

        await (await content.LikeAsync(spammer, postId)).ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");

        // 60 writes a minute, for anyone.
        var writer = await _factory.CreateLoggedInUserAsync(client, "userlimit-defaults-writer");
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/v1/notifications/read-all", new { }, writer.Token)).StatusCode);
        }

        await (await client.PostJsonAsync("/api/v1/notifications/read-all", new { }, writer.Token)).ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
    }

    private async Task<(LoggedInUser Spammer, Guid PostId)> SpammerAndPostAsync(string prefix)
    {
        var author = await _factory.CreateLoggedInUserAsync(_factory.CreateClient(), $"{prefix}-author");
        var spammer = await _factory.CreateLoggedInUserAsync(_factory.CreateClient(), $"{prefix}-spammer");
        return (spammer, await new ContentTestClient(_factory.CreateClient()).PublishAsync(author, "post pod limit"));
    }
}
