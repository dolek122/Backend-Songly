using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Content;

/// <summary>Posts, comments, reactions and the social steps the content tests set up with.</summary>
internal sealed class ContentTestClient(HttpClient client)
{
    public async Task<Guid> PublishAsync(LoggedInUser author, string content = "post") =>
        await IdOf(await client.PostJsonAsync("/api/v1/posts", new { content }, author.Token));

    public Task<HttpResponseMessage> PublishInAsync(LoggedInUser author, Guid communityId, string content = "post w społeczności") =>
        client.PostJsonAsync($"/api/v1/communities/{communityId}/posts", new { content }, author.Token);

    public async Task<Guid> PublishedInAsync(LoggedInUser author, Guid communityId, string content = "post w społeczności")
    {
        var response = await PublishInAsync(author, communityId, content);
        var id = await IdOf(response);
        return id;
    }

    public Task<HttpResponseMessage> GetPostAsync(LoggedInUser viewer, Guid postId) =>
        client.GetAsync($"/api/v1/posts/{postId}", viewer.Token);

    public async Task<JsonElement> PostAsync(LoggedInUser viewer, Guid postId) =>
        await (await GetPostAsync(viewer, postId)).ReadJsonAsync();

    public Task<HttpResponseMessage> DeletePostAsync(LoggedInUser requester, Guid postId) =>
        client.DeleteAsync($"/api/v1/posts/{postId}", requester.Token);

    public Task<HttpResponseMessage> CommentAsync(LoggedInUser author, Guid postId, string content = "komentarz") =>
        client.PostJsonAsync($"/api/v1/posts/{postId}/comments", new { content }, author.Token);

    public async Task<Guid> CommentedAsync(LoggedInUser author, Guid postId, string content = "komentarz") =>
        await IdOf(await CommentAsync(author, postId, content));

    public Task<HttpResponseMessage> DeleteCommentAsync(LoggedInUser requester, Guid postId, Guid commentId) =>
        client.DeleteAsync($"/api/v1/posts/{postId}/comments/{commentId}", requester.Token);

    public Task<HttpResponseMessage> LikeAsync(LoggedInUser user, Guid postId) =>
        client.PutJsonAsync($"/api/v1/posts/{postId}/likes", new { }, user.Token);

    public Task<HttpResponseMessage> UnlikeAsync(LoggedInUser user, Guid postId) =>
        client.DeleteAsync($"/api/v1/posts/{postId}/likes", user.Token);

    public Task<HttpResponseMessage> ShareAsync(LoggedInUser user, Guid postId) =>
        client.PutJsonAsync($"/api/v1/posts/{postId}/shares", new { }, user.Token);

    public Task<HttpResponseMessage> UnshareAsync(LoggedInUser user, Guid postId) =>
        client.DeleteAsync($"/api/v1/posts/{postId}/shares", user.Token);

    /// <summary>A whole page — <c>content</c> and <c>page</c> — of any list endpoint.</summary>
    public async Task<JsonElement> PageAsync(LoggedInUser viewer, string path) =>
        await (await client.GetAsync(path, viewer.Token)).ReadJsonAsync();

    public async Task<IReadOnlyList<string?>> FeedContentsAsync(LoggedInUser viewer) =>
        (await PageAsync(viewer, "/api/v1/feed")).GetProperty("content").EnumerateArray().Select(post => post.GetProperty("content").GetString()).ToList();

    public async Task BefriendAsync(LoggedInUser from, LoggedInUser to)
    {
        var request = await IdOf(await client.PostJsonAsync("/api/v1/friends/requests", new { addresseeId = to.Id }, from.Token));
        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync($"/api/v1/friends/requests/{request}/accept", new { }, to.Token)).StatusCode);
    }

    public async Task BlockAsync(LoggedInUser blocker, LoggedInUser blocked) =>
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutJsonAsync($"/api/v1/blocks/{blocked.Id}", new { }, blocker.Token)).StatusCode);

    private static async Task<Guid> IdOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }
}
