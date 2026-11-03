namespace Songly.IntegrationTests.Content;

/// <summary>The feed: the viewer's and friends' posts, and what friends share. Ported from the original <c>FeedFlowIntegrationTest</c>.</summary>
public sealed class FeedTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());

    [Fact]
    public async Task The_feed_has_own_and_friends_posts_but_not_strangers()
    {
        var ada = await factory.CreateLoggedInUserAsync(_client, "feed-ada");
        var bono = await factory.CreateLoggedInUserAsync(_client, "feed-bono");
        var stranger = await factory.CreateLoggedInUserAsync(_client, "feed-stranger");
        await _content.BefriendAsync(ada, bono);

        await _content.PublishAsync(ada, "post Ady");
        await _content.PublishAsync(bono, "post Bono");
        await _content.PublishAsync(stranger, "post obcego");

        Assert.Equal(["post Bono", "post Ady"], await _content.FeedContentsAsync(ada));
        Assert.Equal(["post obcego"], await _content.FeedContentsAsync(stranger));
    }

    [Fact]
    public async Task The_feed_has_posts_shared_by_friends()
    {
        var ada = await factory.CreateLoggedInUserAsync(_client, "feed-share-ada");
        var bono = await factory.CreateLoggedInUserAsync(_client, "feed-share-bono");
        var stranger = await factory.CreateLoggedInUserAsync(_client, "feed-share-stranger");
        await _content.BefriendAsync(ada, bono);
        var postId = await _content.PublishAsync(stranger, "post obcego do udostępnienia");
        Assert.Empty(await _content.FeedContentsAsync(ada));

        await _content.ShareAsync(bono, postId);

        var feed = (await _content.PageAsync(ada, "/api/v1/feed")).GetProperty("content");
        Assert.Equal(1, feed.GetArrayLength());
        Assert.Equal(postId, feed[0].GetProperty("id").GetGuid());
        Assert.Equal(1, feed[0].GetProperty("shareCount").GetInt64());
    }

    /// <summary>
    /// "No community posts" is a condition on the post, not on the share: otherwise sharing would
    /// smuggle a post from a niche community into the feed of people who never chose it.
    /// </summary>
    [Fact]
    public async Task A_shared_community_post_stays_out_of_the_feed()
    {
        var ada = await factory.CreateLoggedInUserAsync(_client, "feed-cshare-ada");
        var bono = await factory.CreateLoggedInUserAsync(_client, "feed-cshare-bono");
        await _content.BefriendAsync(ada, bono);
        var communityId = await new Community.CommunityTestClient(factory, _client).CreateAsync(bono, "Feed share");
        var postId = await _content.PublishedInAsync(bono, communityId, "post społeczności");

        await _content.ShareAsync(bono, postId);

        Assert.Empty(await _content.FeedContentsAsync(ada));
    }
}
