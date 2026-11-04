using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Content;

/// <summary>Reports, the moderation queue and the setup around them — against any app instance (the shared one, or one with other report settings).</summary>
internal sealed class ReportTestClient(SonglyApiFactory factory, HttpClient client)
{
    public CommunityTestClient Communities { get; } = new(factory, client);

    public ContentTestClient Content { get; } = new(client);

    /// <summary>A community with an author who is a member; the owner moderates it.</summary>
    public async Task<(LoggedInUser Owner, LoggedInUser Author, Guid CommunityId)> CommunityWithAuthorAsync(string prefix)
    {
        var owner = await factory.CreateLoggedInUserAsync(client, $"{prefix}-owner");
        var author = await factory.CreateLoggedInUserAsync(client, $"{prefix}-author");
        var communityId = await Communities.CreateAsync(owner, prefix);
        await Communities.JoinedAsync(author, communityId);
        return (owner, author, communityId);
    }

    public async Task<IReadOnlyList<Guid>> PostsAsync(LoggedInUser author, Guid communityId, int count)
    {
        var posts = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            posts.Add(await Content.PublishedInAsync(author, communityId, $"post {i}"));
        }

        return posts;
    }

    public Task<HttpResponseMessage> ReportAsync(LoggedInUser reporter, Guid postId, string reason = "SPAM", string? details = null) =>
        client.PostJsonAsync($"/api/v1/posts/{postId}/reports", new { reason, details }, reporter.Token);

    public async Task ReportedAsync(LoggedInUser reporter, Guid postId) =>
        Assert.Equal(HttpStatusCode.Accepted, (await ReportAsync(reporter, postId)).StatusCode);

    public Task<HttpResponseMessage> QueueResponseAsync(LoggedInUser moderator, Guid communityId) =>
        client.GetAsync($"/api/v1/communities/{communityId}/post-reports", moderator.Token);

    public async Task<JsonElement> QueueAsync(LoggedInUser moderator, Guid communityId) =>
        (await (await QueueResponseAsync(moderator, communityId)).ReadJsonAsync()).GetProperty("content");

    public Task<HttpResponseMessage> DismissAsync(LoggedInUser moderator, Guid communityId, Guid reportId) =>
        client.PostJsonAsync($"/api/v1/communities/{communityId}/post-reports/{reportId}/dismissal", new { }, moderator.Token);

    /// <summary>Dismisses every pending report in the community's queue.</summary>
    public async Task DismissAllAsync(LoggedInUser moderator, Guid communityId)
    {
        foreach (var reportId in (await QueueAsync(moderator, communityId)).EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList())
        {
            Assert.Equal(HttpStatusCode.NoContent, (await DismissAsync(moderator, communityId, reportId)).StatusCode);
        }
    }

    public async Task RemovedAsync(LoggedInUser moderator, Guid postId) =>
        Assert.Equal(HttpStatusCode.NoContent, (await Content.DeletePostAsync(moderator, postId)).StatusCode);

    /// <summary>
    /// Moves the reporter's reports out of the limit's window: they stop taking a place in the
    /// allowance, their status stays. Without it, "you filed that many this hour" could not be told
    /// from "your allowance is smaller because of your dismissals" — a dismissed report takes its place
    /// in the window like any other.
    /// </summary>
    public Task SlideWindowAsync(LoggedInUser reporter) =>
        factory.ExecuteSqlAsync("UPDATE post_reports SET created_at = now() - interval '2 hours' WHERE reporter_id = @id", ("id", reporter.Id));

    /// <summary>
    /// Ages the reporter's decisions out of the history window (original ADR 0030). The only way to
    /// test the penalty lapsing without waiting: the dates were set with the decisions. The same trick
    /// as <see cref="SlideWindowAsync"/> on another table and for another purpose — memory, not intensity.
    /// </summary>
    public Task AgeDecisionsAsync(LoggedInUser reporter) =>
        factory.ExecuteSqlAsync("UPDATE reporter_decisions SET decided_at = now() - interval '2 hours' WHERE user_id = @id", ("id", reporter.Id));

    public Task<long> DecisionsAsync(LoggedInUser reporter, string outcome) =>
        factory.QuerySingleAsync<long>("SELECT count(*) FROM reporter_decisions WHERE user_id = @id AND outcome = @outcome", ("id", reporter.Id), ("outcome", outcome));
}
