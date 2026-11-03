using System.Net;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// The content part of the original <c>AccountDeletionFlowIntegrationTest</c>: the account's own
/// posts go, its activity under other people's posts goes, and nobody else's data breaks a foreign
/// key on the way.
/// </summary>
public sealed class ContentAccountDeletionTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());

    [Fact]
    public async Task Deleting_an_account_removes_its_posts_and_its_activity_under_other_posts()
    {
        var leaving = await factory.CreateLoggedInUserAsync(_client, "leaving-content");
        var staying = await factory.CreateLoggedInUserAsync(_client, "staying-content");
        var ownPostId = await _content.PublishAsync(leaving, "post usera, który zaraz zniknie");
        var otherPostId = await _content.PublishAsync(staying, "post kogoś innego");
        await _content.LikeAsync(leaving, otherPostId);
        await _content.ShareAsync(leaving, otherPostId);
        await _content.CommentedAsync(leaving, otherPostId, "komentarz przed usunięciem konta");

        // The other way round: someone else's activity under the leaving account's post.
        await _content.LikeAsync(staying, ownPostId);
        await _content.ShareAsync(staying, ownPostId);
        await _content.CommentedAsync(staying, ownPostId);

        Assert.Equal(HttpStatusCode.NoContent, (await new Community.CommunityTestClient(factory, _client).DeleteAccountAsync(leaving)).StatusCode);

        var other = await _content.PostAsync(staying, otherPostId);
        Assert.Equal(0, other.GetProperty("likeCount").GetInt64());
        Assert.Equal(0, other.GetProperty("commentCount").GetInt64());
        Assert.Equal(0, other.GetProperty("shareCount").GetInt64());
        await (await _content.GetPostAsync(staying, ownPostId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        Assert.Equal(0L, await factory.QuerySingleAsync<long>(
            "SELECT (SELECT count(*) FROM posts WHERE author_id = @id) + (SELECT count(*) FROM comments WHERE author_id = @id)"
            + " + (SELECT count(*) FROM likes WHERE user_id = @id) + (SELECT count(*) FROM shares WHERE user_id = @id)",
            ("id", leaving.Id)));
    }
}
