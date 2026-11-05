using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// The inbox filled by other people's activity. Ported from the original
/// <c>NotificationFlowIntegrationTest</c>, with the recording rules of <c>NotificationServiceTest</c>.
/// </summary>
public sealed class NotificationFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());
    private readonly NotificationTestClient _inbox = new(factory.CreateClient());

    [Fact]
    public async Task Social_and_content_activity_produce_notifications_with_a_read_flow()
    {
        var ada = await UserAsync("notif-ada");
        var bono = await UserAsync("notif-bono");

        // A request: a notification for the addressee, with the sender's display data.
        var requestId = await SendFriendRequestAsync(ada, bono);
        Assert.Equal(1, await _inbox.UnreadCountAsync(bono));
        var received = (await _inbox.ListAsync(bono))[0];
        Assert.Equal("FRIEND_REQUEST_RECEIVED", received.GetProperty("type").GetString());
        Assert.Equal(ada.Id, received.GetProperty("actorId").GetGuid());
        Assert.Equal(NameOf(ada), received.GetProperty("actorDisplayName").GetString());
        Assert.Equal(requestId, received.GetProperty("resourceId").GetGuid());

        // Accepting: a notification for the sender.
        Assert.Equal(HttpStatusCode.OK, (await _client.PostJsonAsync($"/api/v1/friends/requests/{requestId}/accept", new { }, bono.Token)).StatusCode);
        var accepted = (await _inbox.ListAsync(ada))[0];
        Assert.Equal("FRIEND_REQUEST_ACCEPTED", accepted.GetProperty("type").GetString());
        Assert.Equal(bono.Id, accepted.GetProperty("actorId").GetGuid());

        // A like and a comment: notifications for the post's author — but not the author's own like.
        var postId = await _content.PublishAsync(ada, "nowy album!");
        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(bono, postId)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _content.CommentAsync(bono, postId, "świetne")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(ada, postId)).StatusCode);
        Assert.Equal(3, await _inbox.UnreadCountAsync(ada));

        // Reading: someone else's notification does not exist for them.
        var first = (await _inbox.ListAsync(ada))[0].GetProperty("id").GetGuid();
        await (await _inbox.MarkReadAsync(bono, first)).ReadErrorAsync(HttpStatusCode.NotFound, "NOTIFICATION_NOT_FOUND");
        Assert.Equal(HttpStatusCode.NoContent, (await _inbox.MarkReadAsync(ada, first)).StatusCode);
        Assert.Equal(2, await _inbox.UnreadCountAsync(ada));

        await _inbox.ReadAllAsync(ada);
        Assert.Equal(0, await _inbox.UnreadCountAsync(ada));
    }

    /// <summary>The newest first; the type and resource say what happened to what.</summary>
    [Fact]
    public async Task The_inbox_lists_the_newest_first()
    {
        var author = await UserAsync("notif-order-author");
        var fan = await UserAsync("notif-order-fan");
        var postId = await _content.PublishAsync(author, "kolejność");

        await _content.LikeAsync(fan, postId);
        await _content.CommentAsync(fan, postId);

        var types = (await _inbox.ListAsync(author)).Select(n => n.GetProperty("type").GetString());
        Assert.Equal(["POST_COMMENTED", "POST_LIKED"], types);
        Assert.All(await _inbox.ListAsync(author), n => Assert.Equal(postId, n.GetProperty("resourceId").GetGuid()));
    }

    /// <summary>Reading again does not move the moment of the first reading.</summary>
    [Fact]
    public async Task Reading_again_keeps_the_first_reading_time()
    {
        var author = await UserAsync("notif-reread-author");
        var fan = await UserAsync("notif-reread-fan");
        await _content.LikeAsync(fan, await _content.PublishAsync(author));
        var id = (await _inbox.ListAsync(author))[0].GetProperty("id").GetGuid();

        await _inbox.MarkReadAsync(author, id);
        var readAt = (await _inbox.ListAsync(author))[0].GetProperty("readAt").GetDateTimeOffset();
        await _inbox.MarkReadAsync(author, id);

        Assert.Equal(readAt, (await _inbox.ListAsync(author))[0].GetProperty("readAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Joining_a_community_notifies_the_owner_once()
    {
        var owner = await UserAsync("notif-community-owner");
        var joiner = await UserAsync("notif-community-joiner");
        var communities = new CommunityTestClient(factory, _client);
        var communityId = await communities.CreateAsync(owner, "Metalheads");

        await communities.JoinedAsync(joiner, communityId);

        var joined = (await _inbox.ListAsync(owner))[0];
        Assert.Equal("COMMUNITY_MEMBER_JOINED", joined.GetProperty("type").GetString());
        Assert.Equal(joiner.Id, joined.GetProperty("actorId").GetGuid());
        Assert.Equal(communityId, joined.GetProperty("resourceId").GetGuid());

        // Joining again (idempotent) is no new membership, so no new notification.
        await communities.JoinedAsync(joiner, communityId);
        Assert.Equal(1, await _inbox.UnreadCountAsync(owner));
    }

    [Fact]
    public async Task Sharing_a_post_notifies_its_author()
    {
        var author = await UserAsync("notif-share-author");
        var sharer = await UserAsync("notif-share-sharer");
        var postId = await _content.PublishAsync(author, "do udostępnienia");

        Assert.Equal(HttpStatusCode.NoContent, (await _content.ShareAsync(sharer, postId)).StatusCode);

        var shared = (await _inbox.ListAsync(author))[0];
        Assert.Equal("POST_SHARED", shared.GetProperty("type").GetString());
        Assert.Equal(sharer.Id, shared.GetProperty("actorId").GetGuid());
    }

    /// <summary>
    /// Original ADR 0035: an undo/redo loop on a like, a share and a membership leaves one notification
    /// per actor and resource in the recipient's inbox — also after the recipient read it.
    /// </summary>
    [Fact]
    public async Task Undo_redo_loops_leave_one_notification_per_actor_and_resource()
    {
        var author = await UserAsync("notif-loop-author");
        var looper = await UserAsync("notif-loop-looper");
        var postId = await _content.PublishAsync(author, "pętla");
        var communities = new CommunityTestClient(factory, _client);
        var communityId = await communities.CreateAsync(author, "Pętla");

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(looper, postId)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await _content.UnlikeAsync(looper, postId)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await _content.ShareAsync(looper, postId)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await _content.UnshareAsync(looper, postId)).StatusCode);
            await communities.JoinedAsync(looper, communityId);
            Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}/membership", looper.Token)).StatusCode);

            // Reading does not open the door to another notification.
            await _inbox.ReadAllAsync(author);
        }

        var page = await _inbox.PageAsync(author);
        Assert.Equal(3, page.GetProperty("page").GetProperty("totalElements").GetInt64());
        Assert.Single(await _inbox.OfTypeAsync(author, "POST_LIKED"));
        Assert.Single(await _inbox.OfTypeAsync(author, "POST_SHARED"));
        Assert.Single(await _inbox.OfTypeAsync(author, "COMMUNITY_MEMBER_JOINED"));
    }

    /// <summary>Another post liked by the same person is another resource — a new notification.</summary>
    [Fact]
    public async Task A_like_of_another_post_is_a_new_notification()
    {
        var author = await UserAsync("notif-two-posts-author");
        var fan = await UserAsync("notif-two-posts-fan");

        await _content.LikeAsync(fan, await _content.PublishAsync(author, "pierwszy"));
        await _content.LikeAsync(fan, await _content.PublishAsync(author, "drugi"));

        Assert.Equal(2, (await _inbox.OfTypeAsync(author, "POST_LIKED")).Count);
    }

    /// <summary>Every comment is new content, not a restored state — never deduplicated.</summary>
    [Fact]
    public async Task Every_comment_is_a_notification()
    {
        var author = await UserAsync("notif-comments-author");
        var talker = await UserAsync("notif-comments-talker");
        var postId = await _content.PublishAsync(author);

        await _content.CommentAsync(talker, postId, "pierwszy");
        await _content.CommentAsync(talker, postId, "drugi");

        Assert.Equal(2, (await _inbox.OfTypeAsync(author, "POST_COMMENTED")).Count);
    }

    /// <summary>
    /// Requests collapse into one only until read (original ADR 0035): a send/cancel loop leaves one
    /// unread notification pointing at the last request, and a new request after reading is a new
    /// message.
    /// </summary>
    [Fact]
    public async Task A_friend_request_loop_collapses_while_unread_and_points_at_the_pending_request()
    {
        var addressee = await UserAsync("notif-req-addressee");
        var looper = await UserAsync("notif-req-looper");

        Guid? lastRequestId = null;
        for (var i = 0; i < 3; i++)
        {
            if (lastRequestId is { } previous)
            {
                Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/friends/requests/{previous}", looper.Token)).StatusCode);
            }

            lastRequestId = await SendFriendRequestAsync(looper, addressee);
        }

        var collapsed = await _inbox.PageAsync(addressee);
        Assert.Equal(1, collapsed.GetProperty("page").GetProperty("totalElements").GetInt64());
        var only = collapsed.GetProperty("content")[0];
        Assert.Equal("FRIEND_REQUEST_RECEIVED", only.GetProperty("type").GetString());
        Assert.Equal(lastRequestId, only.GetProperty("resourceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("readAt").ValueKind);

        await _inbox.ReadAllAsync(addressee);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/friends/requests/{lastRequestId}", looper.Token)).StatusCode);
        var resentRequestId = await SendFriendRequestAsync(looper, addressee);

        Assert.Equal(1, await _inbox.UnreadCountAsync(addressee));
        var page = await _inbox.PageAsync(addressee);
        Assert.Equal(2, page.GetProperty("page").GetProperty("totalElements").GetInt64());
        Assert.Equal(resentRequestId, page.GetProperty("content")[0].GetProperty("resourceId").GetGuid());
    }

    /// <summary>
    /// Two people inviting each other: the second request accepts the first, and the one who asked first
    /// is told — the acceptance happens by itself, but it is still an acceptance.
    /// </summary>
    [Fact]
    public async Task A_mutual_request_notifies_the_first_sender_of_the_acceptance()
    {
        var first = await UserAsync("notif-mutual-first");
        var second = await UserAsync("notif-mutual-second");

        await SendFriendRequestAsync(first, second);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostJsonAsync("/api/v1/friends/requests", new { addresseeId = first.Id }, second.Token)).StatusCode);

        var accepted = Assert.Single(await new NotificationTestClient(_client).OfTypeAsync(first, "FRIEND_REQUEST_ACCEPTED"));
        Assert.Equal(second.Id, accepted.GetProperty("actorId").GetGuid());
    }

    [Fact]
    public async Task The_inbox_requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/notifications", accessToken: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/notifications/unread-count", accessToken: null)).StatusCode);
    }

    private Task<LoggedInUser> UserAsync(string prefix) => factory.CreateLoggedInUserAsync(_client, prefix);

    private async Task<Guid> SendFriendRequestAsync(LoggedInUser from, LoggedInUser to)
    {
        using var response = await _client.PostJsonAsync("/api/v1/friends/requests", new { addresseeId = to.Id }, from.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private static string NameOf(LoggedInUser user) => user.User.Email[..user.User.Email.IndexOf('@', StringComparison.Ordinal)];
}
