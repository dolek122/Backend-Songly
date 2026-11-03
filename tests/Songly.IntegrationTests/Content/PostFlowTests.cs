using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// Posts, comments, likes and shares end to end. Ported from the original
/// <c>ContentFlowIntegrationTest</c>, with the scenarios of its <c>ContentServiceTest</c> that are
/// observable through the API.
/// </summary>
public sealed class PostFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());

    [Fact]
    public async Task A_post_is_created_liked_commented_listed_and_deleted()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "content-author");
        var reader = await factory.CreateLoggedInUserAsync(_client, "content-reader");

        var created = await (await _client.PostJsonAsync("/api/v1/posts", new { content = "Słuchajcie nowego albumu!" }, author.Token)).Content
            .ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var postId = created.GetProperty("id").GetGuid();
        Assert.Equal(NameOf(author), created.GetProperty("authorDisplayName").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("communityId").ValueKind);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("imageUrl").ValueKind);
        Assert.Equal(0, created.GetProperty("likeCount").GetInt64());

        // Liking twice is one like.
        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(reader, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(reader, postId)).StatusCode);

        var comment = await (await _content.CommentAsync(reader, postId, "Świetny!")).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(NameOf(reader), comment.GetProperty("authorDisplayName").GetString());
        Assert.Equal(postId, comment.GetProperty("postId").GetGuid());

        var post = await _content.PostAsync(reader, postId);
        Assert.Equal(1, post.GetProperty("likeCount").GetInt64());
        Assert.True(post.GetProperty("likedByMe").GetBoolean());
        Assert.Equal(1, post.GetProperty("commentCount").GetInt64());
        Assert.Equal(NameOf(author), post.GetProperty("authorDisplayName").GetString());

        // The public list, newest first: the post created a moment ago is on its first page.
        var list = await _content.PageAsync(reader, "/api/v1/posts?size=100");
        Assert.Contains(postId, list.GetProperty("content").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));

        Assert.Equal(HttpStatusCode.NoContent, (await _content.UnlikeAsync(reader, postId)).StatusCode);
        post = await _content.PostAsync(reader, postId);
        Assert.Equal(0, post.GetProperty("likeCount").GetInt64());
        Assert.False(post.GetProperty("likedByMe").GetBoolean());

        await (await _content.DeletePostAsync(reader, postId)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_RESOURCE_OWNER");
        Assert.Equal(HttpStatusCode.NoContent, (await _content.DeletePostAsync(author, postId)).StatusCode);
        await (await _content.GetPostAsync(author, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    /// <summary>Under an ordinary post only authorship counts — the post's author does not moderate the comments.</summary>
    [Fact]
    public async Task Only_the_comments_author_deletes_it()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "comment-author");
        var other = await factory.CreateLoggedInUserAsync(_client, "comment-other");
        var postId = await _content.PublishAsync(author, "Post pod komentarze");
        var commentId = await _content.CommentedAsync(other, postId);

        await (await _content.DeleteCommentAsync(author, postId, commentId)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_RESOURCE_OWNER");
        Assert.Equal(HttpStatusCode.NoContent, (await _content.DeleteCommentAsync(other, postId, commentId)).StatusCode);

        Assert.Equal(0, (await _content.PageAsync(author, $"/api/v1/posts/{postId}/comments")).GetProperty("content").GetArrayLength());
    }

    /// <summary>The comment must belong to the post in the path — a comment id is not enough.</summary>
    [Fact]
    public async Task A_comment_is_found_only_under_its_own_post()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "comment-path");
        var postId = await _content.PublishAsync(author);
        var otherPostId = await _content.PublishAsync(author);
        var commentId = await _content.CommentedAsync(author, postId);

        await (await _content.DeleteCommentAsync(author, otherPostId, commentId)).ReadErrorAsync(HttpStatusCode.NotFound, "COMMENT_NOT_FOUND");
        await (await _content.DeleteCommentAsync(author, postId, Guid.NewGuid())).ReadErrorAsync(HttpStatusCode.NotFound, "COMMENT_NOT_FOUND");
    }

    [Fact]
    public async Task Sharing_updates_the_counters_and_is_idempotent()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "share-author");
        var sharer = await factory.CreateLoggedInUserAsync(_client, "share-sharer");
        var postId = await _content.PublishAsync(author, "Post do udostępnienia");

        Assert.Equal(HttpStatusCode.NoContent, (await _content.ShareAsync(sharer, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _content.ShareAsync(sharer, postId)).StatusCode);

        var post = await _content.PostAsync(sharer, postId);
        Assert.Equal(1, post.GetProperty("shareCount").GetInt64());
        Assert.True(post.GetProperty("sharedByMe").GetBoolean());
        Assert.False((await _content.PostAsync(author, postId)).GetProperty("sharedByMe").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await _content.UnshareAsync(sharer, postId)).StatusCode);
        post = await _content.PostAsync(sharer, postId);
        Assert.Equal(0, post.GetProperty("shareCount").GetInt64());
        Assert.False(post.GetProperty("sharedByMe").GetBoolean());

        // Taking back what is not there is no error.
        Assert.Equal(HttpStatusCode.NoContent, (await _content.UnshareAsync(sharer, postId)).StatusCode);
    }

    [Fact]
    public async Task A_missing_post_cannot_be_liked_shared_or_commented()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "content-missing");
        var missing = Guid.NewGuid();

        await (await _content.LikeAsync(user, missing)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _content.UnlikeAsync(user, missing)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _content.ShareAsync(user, missing)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _content.CommentAsync(user, missing)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _client.GetAsync($"/api/v1/posts/{missing}/comments", user.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _content.DeletePostAsync(user, missing)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_post_needs_content(string? content)
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "content-blank");

        await (await _client.PostJsonAsync("/api/v1/posts", new { content }, user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Content_has_its_length_limits()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "content-long");
        var postId = await _content.PublishAsync(user, new string('a', 1000));

        await (await _client.PostJsonAsync("/api/v1/posts", new { content = new string('a', 1001) }, user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await _content.CommentAsync(user, postId, new string('a', 501))).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.Created, (await _content.CommentAsync(user, postId, new string('a', 500))).StatusCode);
    }

    /// <summary>
    /// An image is attached only by a key the upload issued to this user (original ADR 0048) — so a
    /// key made up, or someone else's, does not attach anything. Uploads come with the next part; until
    /// then no key was ever issued.
    /// </summary>
    [Fact]
    public async Task An_image_key_that_was_never_issued_is_rejected()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "content-image-key");

        await (await _client.PostJsonAsync("/api/v1/posts", new { content = "z obrazkiem", imageKey = "posts/someone-else.jpg" }, user.Token))
            .ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_POST_IMAGE");
    }

    [Fact]
    public async Task Post_endpoints_require_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/posts", accessToken: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/v1/posts", new { content = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/feed", accessToken: null)).StatusCode);
    }

    private static string NameOf(LoggedInUser user) => user.User.Email[..user.User.Email.IndexOf('@', StringComparison.Ordinal)];
}
