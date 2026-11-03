using System.Net;
using Songly.IntegrationTests.Community;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// Posts in communities: publishing, isolation from the feed, moderation of posts and comments,
/// bans, and cleanup after a deleted community. Ported from the original
/// <c>CommunityPostIntegrationTest</c>, <c>CommentModerationIntegrationTest</c> (without the
/// notification and moderation-log parts, which come with those modules) and the content scenarios
/// of <c>CommunityModerationIntegrationTest</c>.
/// </summary>
/// <remarks>
/// The interesting part happens at the module boundary and in the database: <c>posts.community_id</c>
/// references <c>communities</c>, so deleting a community has to go through the event and
/// content's cleanup — only the database can show it accepts the delete.
/// </remarks>
public sealed class CommunityPostTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());
    private readonly CommunityTestClient _communities = new(factory, factory.CreateClient());

    [Fact]
    public async Task A_member_publishes_and_the_post_is_listed_in_the_community()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "cpost-owner");
        var communityId = await _communities.CreateAsync(owner, "Fani Doom Metalu");

        var postId = await _content.PublishedInAsync(owner, communityId, "Nowa płyta Candlemass");

        var posts = (await _content.PageAsync(owner, $"/api/v1/communities/{communityId}/posts")).GetProperty("content");
        Assert.Equal(1, posts.GetArrayLength());
        Assert.Equal(postId, posts[0].GetProperty("id").GetGuid());
        Assert.Equal("Nowa płyta Candlemass", posts[0].GetProperty("content").GetString());
        Assert.Equal(communityId, posts[0].GetProperty("communityId").GetGuid());
    }

    /// <summary>Communities are fully public, so hiding their content from non-members would be a pretence.</summary>
    [Fact]
    public async Task A_non_member_cannot_publish_but_can_read()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "cpost-read-owner");
        var outsider = await factory.CreateLoggedInUserAsync(_client, "cpost-outsider");
        var communityId = await _communities.CreateAsync(owner, "Fani Stoner Rocka");
        await _content.PublishedInAsync(owner, communityId, "Kyuss na zawsze");

        await (await _content.PublishInAsync(outsider, communityId, "wchodzę z buta")).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MEMBER");

        Assert.Equal(1, (await _content.PageAsync(outsider, $"/api/v1/communities/{communityId}/posts")).GetProperty("content").GetArrayLength());
    }

    [Fact]
    public async Task An_unknown_community_is_not_found()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "cpost-unknown");

        await (await _content.PublishInAsync(user, Guid.NewGuid(), "w pustkę")).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
        await (await _client.GetAsync($"/api/v1/communities/{Guid.NewGuid()}/posts", user.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
    }

    /// <summary>
    /// The separation of the two worlds: a post from a niche community must not surface in the feed
    /// or on the author's profile — it would reach people who never chose that community.
    /// </summary>
    [Fact]
    public async Task A_community_post_stays_out_of_the_feed_and_the_authors_timeline()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "cpost-author");
        var friend = await factory.CreateLoggedInUserAsync(_client, "cpost-friend");
        await _content.BefriendAsync(author, friend);
        var communityId = await _communities.CreateAsync(author, "Fani Grindcore");

        var communityPostId = await _content.PublishedInAsync(author, communityId, "post społecznościowy");
        await _content.PublishAsync(author, "post zwykły");

        Assert.Equal(["post zwykły"], await _content.FeedContentsAsync(friend));
        var timeline = (await _content.PageAsync(friend, $"/api/v1/posts?authorId={author.Id}")).GetProperty("content");
        Assert.Equal(["post zwykły"], timeline.EnumerateArray().Select(p => p.GetProperty("content").GetString()));
        Assert.DoesNotContain(communityPostId, (await _content.PageAsync(friend, "/api/v1/posts?size=100")).GetProperty("content").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
    }

    /// <summary>
    /// The minimum of moderation (original Claude.md §8): the owner and the moderators delete any post
    /// in their community — and nobody else does, moderation does not spill over onto everyone.
    /// </summary>
    [Fact]
    public async Task The_owner_and_moderators_delete_someone_elses_post()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "cpost-mod-owner");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "cpost-mod-moderator");
        var member = await factory.CreateLoggedInUserAsync(_client, "cpost-mod-member");
        var outsider = await factory.CreateLoggedInUserAsync(_client, "cpost-mod-outsider");
        var communityId = await _communities.CreateAsync(owner, "Fani Sludge");
        await _communities.JoinedAsync(moderator, communityId);
        await _communities.JoinedAsync(member, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await _communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        var spam = await _content.PublishedInAsync(member, communityId, "spam");
        var moreSpam = await _content.PublishedInAsync(member, communityId, "więcej spamu");
        var ownersPost = await _content.PublishedInAsync(owner, communityId, "post właściciela");

        await (await _content.DeletePostAsync(outsider, spam)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_RESOURCE_OWNER");
        await (await _content.DeletePostAsync(member, ownersPost)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_RESOURCE_OWNER");

        Assert.Equal(HttpStatusCode.NoContent, (await _content.DeletePostAsync(owner, spam)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _content.DeletePostAsync(moderator, moreSpam)).StatusCode);

        var remaining = (await _content.PageAsync(owner, $"/api/v1/communities/{communityId}/posts")).GetProperty("content");
        Assert.Equal([ownersPost], remaining.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
    }

    /// <summary>The owner moderates only at home: an ordinary post is protected by authorship alone.</summary>
    [Fact]
    public async Task A_community_owner_cannot_delete_other_peoples_ordinary_posts()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "cpost-scope-owner");
        var other = await factory.CreateLoggedInUserAsync(_client, "cpost-scope-other");
        await _communities.CreateAsync(owner, "Fani Post-Rocka");
        var postId = await _content.PublishAsync(other, "mój post");

        await (await _content.DeletePostAsync(owner, postId)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_RESOURCE_OWNER");
    }

    /// <summary>
    /// Original ADR 0045: before it, the only answer to a troll's comment was deleting the whole post —
    /// punishing its author for someone else's words.
    /// </summary>
    [Fact]
    public async Task A_moderator_removes_someone_elses_comment()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "comment-mod-owner");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "comment-mod-moderator");
        var author = await factory.CreateLoggedInUserAsync(_client, "comment-mod-author");
        var troll = await factory.CreateLoggedInUserAsync(_client, "comment-mod-troll");
        var communityId = await _communities.CreateAsync(owner, "Komentarze moderacja");
        await _communities.JoinedAsync(moderator, communityId);
        await _communities.JoinedAsync(author, communityId);
        await _communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR");
        var postId = await _content.PublishedInAsync(author, communityId);
        var commentId = await _content.CommentedAsync(troll, postId);

        Assert.Equal(HttpStatusCode.NoContent, (await _content.DeleteCommentAsync(moderator, postId, commentId)).StatusCode);

        Assert.Equal(0, (await _content.PageAsync(author, $"/api/v1/posts/{postId}/comments")).GetProperty("content").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await _content.GetPostAsync(author, postId)).StatusCode);
    }

    [Fact]
    public async Task An_ordinary_member_cannot_remove_someone_elses_comment()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "comment-mod-plain-owner");
        var author = await factory.CreateLoggedInUserAsync(_client, "comment-mod-plain-author");
        var other = await factory.CreateLoggedInUserAsync(_client, "comment-mod-plain-other");
        var communityId = await _communities.CreateAsync(owner, "Komentarze zwykły członek");
        await _communities.JoinedAsync(author, communityId);
        await _communities.JoinedAsync(other, communityId);
        var postId = await _content.PublishedInAsync(author, communityId);
        var commentId = await _content.CommentedAsync(author, postId);

        await (await _content.DeleteCommentAsync(other, postId, commentId)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_RESOURCE_OWNER");
    }

    /// <summary>A moderator's powers stay in their community — under an ordinary post only authorship counts.</summary>
    [Fact]
    public async Task Moderation_does_not_reach_comments_under_ordinary_posts()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "comment-mod-regular-owner");
        var author = await factory.CreateLoggedInUserAsync(_client, "comment-mod-regular-author");
        await _communities.CreateAsync(owner, "Komentarze poza społecznością");
        var postId = await _content.PublishAsync(author);
        var commentId = await _content.CommentedAsync(author, postId);

        await (await _content.DeleteCommentAsync(owner, postId, commentId)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_RESOURCE_OWNER");
    }

    /// <summary>
    /// A ban cuts off <b>writing</b>, not reading: communities are public, so hiding their content
    /// would be a pretence — logging out is enough. Publishing goes with the membership the ban
    /// removed; commenting needs only a sign-in, so it has a check of its own.
    /// </summary>
    [Fact]
    public async Task A_banned_user_still_reads_but_cannot_publish_or_comment()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "ban-read-owner");
        var troll = await factory.CreateLoggedInUserAsync(_client, "ban-read-troll");
        var communityId = await _communities.CreateAsync(owner, "Czytelnia");
        await _communities.JoinedAsync(troll, communityId);
        var postId = await _content.PublishedInAsync(owner, communityId, "treść społeczności");

        await _communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(1, (await _content.PageAsync(troll, $"/api/v1/communities/{communityId}/posts")).GetProperty("content").GetArrayLength());
        await (await _content.PublishInAsync(troll, communityId, "wracam")).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MEMBER");
        await (await _content.CommentAsync(troll, postId, "dalej zaczepiam")).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");

        // Likes and shares stay: pure numbers, without identity or text.
        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(troll, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _content.ShareAsync(troll, postId)).StatusCode);
    }

    /// <summary>An expired ban counts nowhere (original ADR 0014) — commenting is open again without anyone's action.</summary>
    [Fact]
    public async Task An_expired_ban_no_longer_blocks_comments()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "ban-expired-owner");
        var troll = await factory.CreateLoggedInUserAsync(_client, "ban-expired-troll");
        var communityId = await _communities.CreateAsync(owner, "Czytelnia po banie");
        var postId = await _content.PublishedInAsync(owner, communityId);
        await _communities.BannedAsync(owner, communityId, troll.Id, durationDays: 1);

        await _communities.ExpireBanAsync(communityId, troll.Id);

        Assert.Equal(HttpStatusCode.Created, (await _content.CommentAsync(troll, postId)).StatusCode);
    }

    /// <summary><c>posts.community_id</c> references <c>communities</c>: without content's cleanup, the database would refuse the delete.</summary>
    [Fact]
    public async Task Deleting_a_community_removes_its_posts()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "cpost-del-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, "cpost-del-member");
        var communityId = await _communities.CreateAsync(owner, "Fani Krautrocka");
        await _communities.JoinedAsync(member, communityId);
        var postId = await _content.PublishedInAsync(member, communityId, "Can - Tago Mago");
        await _content.CommentedAsync(owner, postId);
        await _content.LikeAsync(owner, postId);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}", owner.Token)).StatusCode);

        await (await _content.GetPostAsync(member, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    /// <summary>
    /// The same trap on the second path: deleting the owner's account deletes their communities, and
    /// the posts in them must go too — other people's as well. A bulk delete of the communities would
    /// publish no event.
    /// </summary>
    [Fact]
    public async Task Deleting_the_owners_account_removes_other_users_posts_in_their_communities()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "cpost-acc-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, "cpost-acc-member");
        var communityId = await _communities.CreateAsync(owner, "Fani Shoegaze");
        await _communities.JoinedAsync(member, communityId);
        var postId = await _content.PublishedInAsync(member, communityId, "Loveless");

        Assert.Equal(HttpStatusCode.NoContent, (await _communities.DeleteAccountAsync(owner)).StatusCode);

        await (await _content.GetPostAsync(member, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }
}
