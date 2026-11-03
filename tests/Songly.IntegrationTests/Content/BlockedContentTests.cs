using System.Net;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// Blocks enforced in the content module (original Claude.md §9). Ported from the original
/// <c>BlockedContentIntegrationTest</c>, plus its unit tests' "unliking works despite a block".
/// </summary>
/// <remarks>
/// Integration tests, because the filter sits in the database queries (filtering after reading a
/// page would break paging): only Postgres can say the rows were really left out and
/// <c>totalElements</c> agrees.
/// </remarks>
public sealed class BlockedContentTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());

    /// <summary>
    /// The precondition of everything else: an empty set of blocks is the case of nearly every
    /// user, and <c>not in</c> over an empty list is the classic SQL trap — <c>x not in (null)</c>
    /// is true for no row, so a naive implementation blanks the feed for everyone at once.
    /// </summary>
    [Fact]
    public async Task A_user_without_blocks_sees_posts_normally()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "block-none-viewer");
        var postId = await _content.PublishAsync(viewer, "widoczny dla wszystkich");
        await _content.CommentedAsync(viewer, postId, "własny komentarz");

        Assert.Equal(1, (await _content.PageAsync(viewer, "/api/v1/feed")).GetProperty("page").GetProperty("totalElements").GetInt64());
        Assert.True((await _content.PageAsync(viewer, "/api/v1/posts")).GetProperty("content").GetArrayLength() >= 1);
        Assert.Equal(1, (await _content.PageAsync(viewer, $"/api/v1/posts/{postId}/comments")).GetProperty("page").GetProperty("totalElements").GetInt64());

        // The comment counter filters by the same empty set.
        Assert.Equal(1, (await _content.PostAsync(viewer, postId)).GetProperty("commentCount").GetInt64());
    }

    [Fact]
    public async Task A_blocked_authors_post_disappears_from_lists_and_direct_reads()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "block-hide-author");
        var blocker = await factory.CreateLoggedInUserAsync(_client, "block-hide-blocker");
        var postId = await _content.PublishAsync(author, "treść zablokowanego");

        await _content.BlockAsync(blocker, author);

        await (await _content.GetPostAsync(blocker, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        var timeline = await _content.PageAsync(blocker, $"/api/v1/posts?authorId={author.Id}");
        Assert.Equal(0, timeline.GetProperty("content").GetArrayLength());
        Assert.Equal(0, timeline.GetProperty("page").GetProperty("totalElements").GetInt64());
        Assert.DoesNotContain(postId, (await _content.PageAsync(blocker, "/api/v1/posts?size=100")).GetProperty("content").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
    }

    /// <summary>
    /// Both ways: the blocked user loses sight as the blocker does. Otherwise a block would only
    /// cover one's own eyes, while the other side kept following everything we write.
    /// </summary>
    [Fact]
    public async Task A_block_works_both_ways()
    {
        var blocker = await factory.CreateLoggedInUserAsync(_client, "block-sym-blocker");
        var blocked = await factory.CreateLoggedInUserAsync(_client, "block-sym-blocked");
        var postId = await _content.PublishAsync(blocker, "treść blokującego");

        await _content.BlockAsync(blocker, blocked);

        await (await _content.GetPostAsync(blocked, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    /// <summary>
    /// The point of a block. A like and a comment end in a notification to the author, so without
    /// this check blocking would not cut the most annoying channel of contact the product has.
    /// </summary>
    [Fact]
    public async Task The_blocked_user_cannot_interact_with_the_blockers_post()
    {
        var blocker = await factory.CreateLoggedInUserAsync(_client, "block-write-blocker");
        var blocked = await factory.CreateLoggedInUserAsync(_client, "block-write-blocked");
        var postId = await _content.PublishAsync(blocker, "mój post");

        await _content.BlockAsync(blocker, blocked);

        await (await _content.LikeAsync(blocked, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _content.ShareAsync(blocked, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _content.CommentAsync(blocked, postId, "zaczepka")).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _client.GetAsync($"/api/v1/posts/{postId}/comments", blocked.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    /// <summary>
    /// A block cuts one off from someone else's content; it does not freeze one's own traces on it.
    /// Someone who blocks a user after liking their post must be able to take the like back.
    /// </summary>
    [Fact]
    public async Task Own_likes_and_shares_can_be_taken_back_despite_a_block()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "block-undo-author");
        var fan = await factory.CreateLoggedInUserAsync(_client, "block-undo-fan");
        var postId = await _content.PublishAsync(author);
        await _content.LikeAsync(fan, postId);
        await _content.ShareAsync(fan, postId);

        await _content.BlockAsync(fan, author);

        Assert.Equal(HttpStatusCode.NoContent, (await _content.UnlikeAsync(fan, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _content.UnshareAsync(fan, postId)).StatusCode);
        var post = await _content.PostAsync(author, postId);
        Assert.Equal(0, post.GetProperty("likeCount").GetInt64());
        Assert.Equal(0, post.GetProperty("shareCount").GetInt64());
    }

    /// <summary>
    /// Under a third person's post the two sides meet without any relation between them. The counter
    /// must go down with the list, or the post would say "2 comments" above a list of one.
    /// </summary>
    [Fact]
    public async Task A_blocked_users_comment_vanishes_from_the_list_and_the_count()
    {
        var host = await factory.CreateLoggedInUserAsync(_client, "block-comment-host");
        var blocker = await factory.CreateLoggedInUserAsync(_client, "block-comment-blocker");
        var troll = await factory.CreateLoggedInUserAsync(_client, "block-comment-troll");
        var postId = await _content.PublishAsync(host, "post trzeciej osoby");
        await _content.CommentedAsync(troll, postId, "od trolla");
        await _content.CommentedAsync(blocker, postId, "od blokującego");

        await _content.BlockAsync(blocker, troll);

        var comments = await _content.PageAsync(blocker, $"/api/v1/posts/{postId}/comments");
        Assert.Equal(["od blokującego"], comments.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("content").GetString()));
        Assert.Equal(1, comments.GetProperty("page").GetProperty("totalElements").GetInt64());
        Assert.Equal(1, (await _content.PostAsync(blocker, postId)).GetProperty("commentCount").GetInt64());

        // Everyone else still sees both.
        Assert.Equal(2, (await _content.PostAsync(host, postId)).GetProperty("commentCount").GetInt64());
    }

    /// <summary>
    /// The only way the blocked user's content came back to the feed once the friendship ended:
    /// shared by a mutual friend. That is why the hidden authors are a condition of their own in the
    /// feed query.
    /// </summary>
    [Fact]
    public async Task A_friends_share_of_a_blocked_authors_post_stays_out_of_the_feed()
    {
        var blocker = await factory.CreateLoggedInUserAsync(_client, "block-feed-blocker");
        var friend = await factory.CreateLoggedInUserAsync(_client, "block-feed-friend");
        var troll = await factory.CreateLoggedInUserAsync(_client, "block-feed-troll");
        await _content.BefriendAsync(blocker, friend);
        var trollPostId = await _content.PublishAsync(troll, "treść trolla");
        await _content.BlockAsync(blocker, troll);

        Assert.Equal(HttpStatusCode.NoContent, (await _content.ShareAsync(friend, trollPostId)).StatusCode);

        Assert.Equal(0, (await _content.PageAsync(blocker, "/api/v1/feed")).GetProperty("page").GetProperty("totalElements").GetInt64());
    }

    /// <summary>A community is public, but a block is a personal boundary — it holds in someone else's "house" too.</summary>
    [Fact]
    public async Task A_blocked_authors_post_is_hidden_inside_a_community()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "block-comm-owner");
        var troll = await factory.CreateLoggedInUserAsync(_client, "block-comm-troll");
        var communities = new Community.CommunityTestClient(factory, _client);
        var communityId = await communities.CreateAsync(owner, "Blokady i społeczności");
        await communities.JoinedAsync(troll, communityId);
        await _content.PublishedInAsync(troll, communityId, "spam w społeczności");
        Assert.Equal(1, (await _content.PageAsync(owner, $"/api/v1/communities/{communityId}/posts")).GetProperty("page").GetProperty("totalElements").GetInt64());

        await _content.BlockAsync(owner, troll);

        Assert.Equal(0, (await _content.PageAsync(owner, $"/api/v1/communities/{communityId}/posts")).GetProperty("page").GetProperty("totalElements").GetInt64());
    }
}
