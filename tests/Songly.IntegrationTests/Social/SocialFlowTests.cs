using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Social;

/// <summary>Friend requests, friendships and blocks. Ported from the original <c>SocialFlowIntegrationTest</c> and <c>SocialGraphServiceTest</c>.</summary>
public sealed class SocialFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Send_accept_list_and_unfriend()
    {
        var (ada, bono) = await Pair("social-flow");

        var sent = await Send(ada, bono.Id);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var requestId = (await Json(sent)).GetProperty("id").GetGuid();

        var incoming = (await Get(bono, "/api/v1/friends/requests?direction=INCOMING")).GetProperty("content")[0];
        Assert.Equal(requestId, incoming.GetProperty("id").GetGuid());
        Assert.Equal(ada.User.Email.Split('@')[0], incoming.GetProperty("requesterDisplayName").GetString());

        var accepted = await Json(await Post(bono, $"/api/v1/friends/requests/{requestId}/accept"));
        Assert.Equal("ACCEPTED", accepted.GetProperty("status").GetString());

        var adasFriend = (await Get(ada, "/api/v1/friends")).GetProperty("content")[0];
        Assert.Equal(bono.Id, adasFriend.GetProperty("userId").GetGuid());
        Assert.Equal(bono.User.Email.Split('@')[0], adasFriend.GetProperty("displayName").GetString());
        Assert.Equal(ada.Id, (await Get(bono, "/api/v1/friends")).GetProperty("content")[0].GetProperty("userId").GetGuid());

        await (await Send(ada, bono.Id)).ReadErrorAsync(HttpStatusCode.Conflict, "ALREADY_FRIENDS");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/friends/{bono.Id}", ada.Token)).StatusCode);
        Assert.Equal(0, (await Get(bono, "/api/v1/friends")).GetProperty("content").GetArrayLength());
        await (await _client.DeleteAsync($"/api/v1/friends/{bono.Id}", ada.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "FRIENDSHIP_NOT_FOUND");
    }

    /// <summary>Only the addressee answers; to anyone else the request does not exist.</summary>
    [Fact]
    public async Task A_request_can_be_declined_and_is_accepted_only_by_the_addressee()
    {
        var (ada, bono) = await Pair("social-decline");
        var requestId = await SendId(ada, bono.Id);

        await (await Post(ada, $"/api/v1/friends/requests/{requestId}/accept")).ReadErrorAsync(HttpStatusCode.NotFound, "FRIEND_REQUEST_NOT_FOUND");

        Assert.Equal("DECLINED", (await Json(await Post(bono, $"/api/v1/friends/requests/{requestId}/decline"))).GetProperty("status").GetString());
        await (await Post(bono, $"/api/v1/friends/requests/{requestId}/accept")).ReadErrorAsync(HttpStatusCode.Conflict, "FRIEND_REQUEST_ALREADY_HANDLED");
    }

    /// <summary>Two requests the opposite way are one friendship, not two pending requests.</summary>
    [Fact]
    public async Task Mutual_requests_become_a_friendship_instead_of_a_duplicate()
    {
        var (ada, bono) = await Pair("social-mutual");
        await SendId(ada, bono.Id);

        var second = await Send(bono, ada.Id);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal("ACCEPTED", (await Json(second)).GetProperty("status").GetString());
        Assert.Equal(1, (await Get(ada, "/api/v1/friends")).GetProperty("content").GetArrayLength());
    }

    [Fact]
    public async Task Blocking_ends_the_relation_and_prevents_new_requests_until_unblocked()
    {
        var (ada, bono) = await Pair("social-block");
        var requestId = await SendId(ada, bono.Id);
        await Post(bono, $"/api/v1/friends/requests/{requestId}/accept");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/blocks/{bono.Id}", new { }, ada.Token)).StatusCode);

        Assert.Equal(0, (await Get(ada, "/api/v1/friends")).GetProperty("content").GetArrayLength());
        var blocked = (await Get(ada, "/api/v1/blocks")).GetProperty("content")[0];
        Assert.Equal(bono.Id, blocked.GetProperty("userId").GetGuid());
        Assert.Equal(bono.User.Email.Split('@')[0], blocked.GetProperty("displayName").GetString());

        // Either way: the blocked one cannot ask, and neither can the one who blocked.
        await (await Send(bono, ada.Id)).ReadErrorAsync(HttpStatusCode.Conflict, "USER_BLOCKED");
        await (await Send(ada, bono.Id)).ReadErrorAsync(HttpStatusCode.Conflict, "USER_BLOCKED");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/blocks/{bono.Id}", ada.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(bono, ada.Id)).StatusCode);
    }

    /// <summary>A block removes pending requests in both directions — they cannot be accepted after unblocking.</summary>
    [Fact]
    public async Task Blocking_removes_pending_requests_both_ways_and_is_idempotent()
    {
        var (ada, bono) = await Pair("social-block-pending");
        var requestId = await SendId(bono, ada.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/blocks/{bono.Id}", new { }, ada.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/blocks/{bono.Id}", new { }, ada.Token)).StatusCode);

        Assert.Equal(1, (await Get(ada, "/api/v1/blocks")).GetProperty("content").GetArrayLength());
        Assert.Equal(0, (await Get(ada, "/api/v1/friends/requests?direction=INCOMING")).GetProperty("content").GetArrayLength());
        await (await Post(ada, $"/api/v1/friends/requests/{requestId}/accept")).ReadErrorAsync(HttpStatusCode.NotFound, "FRIEND_REQUEST_NOT_FOUND");
    }

    /// <summary>
    /// Only the one who blocks sees the block. The blocked one gets "you do not block them" — the
    /// truth about their own decision — not the news that someone blocked them (original ADR 0039).
    /// </summary>
    [Fact]
    public async Task The_block_state_is_visible_only_in_the_blockers_direction()
    {
        var (ada, bono) = await Pair("social-status");
        Assert.False((await Get(ada, $"/api/v1/blocks/{bono.Id}")).GetProperty("blocked").GetBoolean());

        await _client.PutJsonAsync($"/api/v1/blocks/{bono.Id}", new { }, ada.Token);

        Assert.True((await Get(ada, $"/api/v1/blocks/{bono.Id}")).GetProperty("blocked").GetBoolean());
        Assert.False((await Get(bono, $"/api/v1/blocks/{ada.Id}")).GetProperty("blocked").GetBoolean());

        await _client.DeleteAsync($"/api/v1/blocks/{bono.Id}", ada.Token);
        Assert.False((await Get(ada, $"/api/v1/blocks/{bono.Id}")).GetProperty("blocked").GetBoolean());
    }

    [Fact]
    public async Task A_request_is_cancelled_by_its_sender_and_not_by_the_addressee()
    {
        var (ada, bono) = await Pair("social-cancel");
        var requestId = await SendId(ada, bono.Id);

        // To the addressee someone else's request is invisible (404): relations are not revealed.
        await (await _client.DeleteAsync($"/api/v1/friends/requests/{requestId}", bono.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "FRIEND_REQUEST_NOT_FOUND");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/friends/requests/{requestId}", ada.Token)).StatusCode);

        Assert.Equal(0, (await Get(ada, "/api/v1/friends/requests?direction=OUTGOING")).GetProperty("content").GetArrayLength());
        Assert.Equal(0, (await Get(bono, "/api/v1/friends/requests?direction=INCOMING")).GetProperty("content").GetArrayLength());

        // Really deleted, not hidden: the same request can be sent again.
        Assert.Equal(HttpStatusCode.Created, (await Send(ada, bono.Id)).StatusCode);
    }

    [Fact]
    public async Task Cancelling_an_accepted_request_is_a_conflict()
    {
        var (ada, bono) = await Pair("social-cancel-accepted");
        var requestId = await SendId(ada, bono.Id);
        await Post(bono, $"/api/v1/friends/requests/{requestId}/accept");

        await (await _client.DeleteAsync($"/api/v1/friends/requests/{requestId}", ada.Token)).ReadErrorAsync(HttpStatusCode.Conflict, "FRIEND_REQUEST_ALREADY_HANDLED");
    }

    [Fact]
    public async Task Requests_to_oneself_to_nobody_or_twice_are_refused()
    {
        var (ada, bono) = await Pair("social-refused");

        await (await Send(ada, ada.Id)).ReadErrorAsync(HttpStatusCode.BadRequest, "SELF_ACTION_NOT_ALLOWED");
        await (await Send(ada, Guid.NewGuid())).ReadErrorAsync(HttpStatusCode.NotFound, "USER_NOT_FOUND");
        await (await _client.PostJsonAsync("/api/v1/friends/requests", new { }, ada.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await SendId(ada, bono.Id);
        await (await Send(ada, bono.Id)).ReadErrorAsync(HttpStatusCode.Conflict, "FRIEND_REQUEST_ALREADY_PENDING");
        await (await _client.PutJsonAsync($"/api/v1/blocks/{ada.Id}", new { }, ada.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "SELF_ACTION_NOT_ALLOWED");
        await (await _client.PutJsonAsync($"/api/v1/blocks/{Guid.NewGuid()}", new { }, ada.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "USER_NOT_FOUND");
    }

    /// <summary>Spring bound the direction by its exact name; Minimal APIs alone would take "incoming" or "0" too.</summary>
    [Theory]
    [InlineData("incoming")]
    [InlineData("0")]
    [InlineData("SIDEWAYS")]
    public async Task An_unknown_direction_is_a_bad_request(string direction)
    {
        var (ada, _) = await Pair("social-direction");

        await (await _client.GetAsync($"/api/v1/friends/requests?direction={direction}", ada.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_PARAMETER");
    }

    /// <summary>The direction defaults to incoming.</summary>
    [Fact]
    public async Task Without_a_direction_the_incoming_requests_are_listed()
    {
        var (ada, bono) = await Pair("social-default-direction");
        var requestId = await SendId(ada, bono.Id);

        Assert.Equal(requestId, (await Get(bono, "/api/v1/friends/requests")).GetProperty("content")[0].GetProperty("id").GetGuid());
        Assert.Equal(0, (await Get(ada, "/api/v1/friends/requests")).GetProperty("content").GetArrayLength());
    }

    /// <summary>
    /// Many pairs, in both orders: the schema's CHECK (<c>user_a_id &lt; user_b_id</c>, compared the
    /// way Postgres compares uuids) refuses a friendship stored in the wrong order.
    /// </summary>
    [Fact]
    public async Task Friendships_between_any_two_users_are_stored_in_the_order_the_schema_requires()
    {
        var users = new List<LoggedInUser>();
        for (var i = 0; i < 6; i++)
        {
            users.Add(await factory.CreateLoggedInUserAsync(_client, "social-order"));
        }

        for (var i = 0; i < users.Count; i++)
        {
            for (var j = i + 1; j < users.Count; j++)
            {
                var requestId = await SendId(users[i], users[j].Id);
                Assert.Equal(HttpStatusCode.OK, (await Post(users[j], $"/api/v1/friends/requests/{requestId}/accept")).StatusCode);
            }
        }

        Assert.Equal(users.Count - 1, (await Get(users[0], "/api/v1/friends")).GetProperty("page").GetProperty("totalElements").GetInt64());
    }

    /// <summary>Deleting an account takes its requests, friendships and blocks along — on either side.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_its_social_data()
    {
        var (leaving, friend) = await Pair("social-delete");
        var blocker = await factory.CreateLoggedInUserAsync(_client, "social-delete-blocker");
        var asked = await factory.CreateLoggedInUserAsync(_client, "social-delete-asked");
        await Post(friend, $"/api/v1/friends/requests/{await SendId(leaving, friend.Id)}/accept");
        await _client.PutJsonAsync($"/api/v1/blocks/{leaving.Id}", new { }, blocker.Token);
        await SendId(leaving, asked.Id);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/me") { Content = JsonContent.Create(new { password = leaving.User.Password }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", leaving.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(0, (await Get(friend, "/api/v1/friends")).GetProperty("content").GetArrayLength());
        Assert.Equal(0, (await Get(blocker, "/api/v1/blocks")).GetProperty("content").GetArrayLength());
        Assert.Equal(0, (await Get(asked, "/api/v1/friends/requests")).GetProperty("content").GetArrayLength());
    }

    [Theory]
    [InlineData("/api/v1/friends")]
    [InlineData("/api/v1/friends/requests")]
    [InlineData("/api/v1/blocks")]
    public async Task Social_endpoints_require_authentication(string path) =>
        await (await _client.GetAsync(path, accessToken: null)).ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");

    private async Task<(LoggedInUser, LoggedInUser)> Pair(string prefix) =>
        (await factory.CreateLoggedInUserAsync(_client, $"{prefix}-ada"), await factory.CreateLoggedInUserAsync(_client, $"{prefix}-bono"));

    private Task<HttpResponseMessage> Send(LoggedInUser from, Guid to) =>
        _client.PostJsonAsync("/api/v1/friends/requests", new { addresseeId = to }, from.Token);

    private async Task<Guid> SendId(LoggedInUser from, Guid to)
    {
        var response = await Send(from, to);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Post(LoggedInUser user, string path) => _client.PostJsonAsync(path, new { }, user.Token);

    private async Task<JsonElement> Get(LoggedInUser user, string path) => await (await _client.GetAsync(path, user.Token)).ReadJsonAsync();

    private static Task<JsonElement> Json(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
}
