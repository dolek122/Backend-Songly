using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;
using StackExchange.Redis;

namespace Songly.IntegrationTests.NowPlaying;

/// <summary>
/// The friends' statuses on the live stream, through the Redis channel. Ported from the stream
/// scenarios of the original <c>NowPlayingIntegrationTest</c>, <c>NowPlayingStreamBroadcasterTest</c>,
/// <c>NowPlayingStreamHubTest</c> and the publication scenarios of <c>NowPlayingServiceRefreshTest</c>.
/// </summary>
/// <remarks>
/// Every change goes through the real channel, also when the stream is held by the same instance that
/// published — there is one delivery path, and this is it.
/// </remarks>
public sealed class LiveStreamTests(SonglyApiFactory factory)
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _social = new(factory.CreateClient());

    [Fact]
    public async Task The_stream_is_an_event_stream()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "stream-open");

        await using var stream = await LiveStream.OpenAsync(_client, viewer);

        Assert.Equal("text/event-stream", stream.Response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>A friend's change reaches the stream with their display data; a stranger's does not.</summary>
    [Fact]
    public async Task A_friends_status_reaches_the_stream_and_a_strangers_does_not()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "stream-viewer");
        var friend = await factory.CreateLoggedInUserAsync(_client, "stream-friend");
        var stranger = await factory.CreateLoggedInUserAsync(_client, "stream-stranger");
        await _social.BefriendAsync(viewer, friend);
        await using var stream = await LiveStream.OpenAsync(_client, viewer);

        await SetManualAsync(stranger, "Obcy utwór");
        await SetManualAsync(friend, "Bohemian Rhapsody");

        var push = await stream.NextAsync();
        Assert.Equal(friend.Id, push.GetProperty("userId").GetGuid());
        Assert.Equal(NameOf(friend), push.GetProperty("displayName").GetString());
        Assert.Equal("Bohemian Rhapsody", push.GetProperty("status").GetProperty("title").GetString());
        Assert.Equal("Tidal", push.GetProperty("status").GetProperty("provider").GetString());
        Assert.True(await stream.NothingWithinAsync(Quiet));
    }

    /// <summary>Clearing the manual status with nothing underneath tells friends "stopped listening" — <c>status: null</c>.</summary>
    [Fact]
    public async Task Clearing_the_manual_status_pushes_stopped()
    {
        var (_, friend, stream) = await WatchingAsync("stream-clear");
        await using var _ = stream;
        await SetManualAsync(friend, "Utwór");
        await stream.NextAsync();

        await _client.DeleteAsync("/api/v1/now-playing/manual", friend.Token);

        var push = await stream.NextAsync();
        Assert.Equal(friend.Id, push.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.Null, push.GetProperty("status").ValueKind);
    }

    /// <summary>A hidden status does not reach the stream (original ADR 0007) — checked on every push, not when the stream opens.</summary>
    [Fact]
    public async Task A_hidden_status_does_not_reach_the_stream()
    {
        var (_, friend, stream) = await WatchingAsync("stream-hidden");
        await using var _ = stream;
        await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = false }, friend.Token);
        await stream.NextAsync();

        await SetManualAsync(friend, "Ukryty utwór");

        Assert.True(await stream.NothingWithinAsync(Quiet));
    }

    /// <summary>
    /// Turning sharing off reaches an open stream as "stopped listening" — the flag alone would leave the
    /// last status on the friend's screen for ever. Turning it back on pushes the current status at once.
    /// </summary>
    [Fact]
    public async Task Turning_sharing_off_and_on_reaches_an_open_stream()
    {
        var (_, friend, stream) = await WatchingAsync("stream-sharing");
        await using var _ = stream;
        await SetManualAsync(friend, "Widoczny utwór");
        await stream.NextAsync();

        await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = false }, friend.Token);
        Assert.Equal(JsonValueKind.Null, (await stream.NextAsync()).GetProperty("status").ValueKind);

        await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = true }, friend.Token);
        Assert.Equal("Widoczny utwór", (await stream.NextAsync()).GetProperty("status").GetProperty("title").GetString());
    }

    /// <summary>
    /// A suspended recipient with an open stream gets nothing (original ADR 0049): the stream lives up to
    /// 30 minutes, and the suspension works at once.
    /// </summary>
    [Fact]
    public async Task A_suspended_viewer_with_an_open_stream_receives_nothing()
    {
        var (viewer, friend, stream) = await WatchingAsync("stream-suspended");
        await using var _ = stream;

        await factory.SuspendAsync(viewer.User);
        await SetManualAsync(friend, "Utwór");

        Assert.True(await stream.NothingWithinAsync(Quiet));
    }

    /// <summary>
    /// A suspended friend's status does not leave the server either. A suspended account cannot change
    /// its status through the API, but the server can — the poll runs on stored tokens — and another
    /// instance may publish it; so the change is published here as another instance would, straight to
    /// the channel, and the broadcaster must drop it.
    /// </summary>
    [Fact]
    public async Task A_suspended_friends_status_does_not_reach_the_stream()
    {
        var (_, friend, stream) = await WatchingAsync("stream-suspended-friend");
        await using var _ = stream;

        await factory.SuspendAsync(friend.User);
        await PublishFromAnotherInstanceAsync(friend, "Utwór zawieszonego");

        Assert.True(await stream.NothingWithinAsync(Quiet));
    }

    /// <summary>The same message from another instance does reach the stream when nobody is suspended — the test above is not passing for nothing.</summary>
    [Fact]
    public async Task A_change_published_by_another_instance_reaches_the_stream()
    {
        var (_, friend, stream) = await WatchingAsync("stream-other-instance");
        await using var _ = stream;

        await PublishFromAnotherInstanceAsync(friend, "Z innej instancji");

        Assert.Equal("Z innej instancji", (await stream.NextAsync()).GetProperty("status").GetProperty("title").GetString());
    }

    /// <summary>A deleted account stops "playing" on its friends' screens at once, not at their next reconnect.</summary>
    [Fact]
    public async Task Deleting_the_account_pushes_stopped()
    {
        var (_, friend, stream) = await WatchingAsync("stream-deleted");
        await using var _ = stream;
        await SetManualAsync(friend, "Utwór");
        await stream.NextAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await new CommunityTestClient(factory, _client).DeleteAccountAsync(friend)).StatusCode);

        var push = await stream.NextAsync();
        Assert.Equal(friend.Id, push.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.Null, push.GetProperty("status").ValueKind);
    }

    /// <summary>One client cannot hold an instance with any number of connections: the extra one is a 429, the earlier ones keep working.</summary>
    [Fact]
    public async Task Streams_above_the_limit_per_user_are_refused()
    {
        var (viewer, friend, first) = await WatchingAsync("stream-limit");
        await using var _ = first;
        await using var second = await LiveStream.OpenAsync(_client, viewer);
        await using var third = await LiveStream.OpenAsync(_client, viewer);

        using var fourth = await OpenRawAsync(viewer);
        await fourth.ReadErrorAsync(HttpStatusCode.TooManyRequests, "TOO_MANY_STREAMS");

        await SetManualAsync(friend, "Utwór");
        await first.NextAsync();
        await third.NextAsync();
    }

    /// <summary>
    /// A closed stream frees its place. The server learns of the close asynchronously — the client is
    /// gone before the request's cancellation reaches the endpoint — so the place is free <b>soon</b>, not
    /// at the instant the client hangs up: the test waits for it instead of assuming it.
    /// </summary>
    [Fact]
    public async Task A_closed_stream_frees_its_place()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "stream-free");
        await using var first = await LiveStream.OpenAsync(_client, viewer);
        await using var second = await LiveStream.OpenAsync(_client, viewer);
        var third = await LiveStream.OpenAsync(_client, viewer);
        using (var refused = await OpenRawAsync(viewer))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        }

        await third.DisposeAsync();

        await WaitUntilAsync(async () =>
        {
            using var response = await OpenRawAsync(viewer);
            return response.StatusCode == HttpStatusCode.OK;
        });
    }

    [Fact]
    public async Task The_stream_requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/now-playing/stream", accessToken: null)).StatusCode);
    }

    private async Task<(LoggedInUser Viewer, LoggedInUser Friend, LiveStream Stream)> WatchingAsync(string prefix)
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-viewer");
        var friend = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-friend");
        await _social.BefriendAsync(viewer, friend);
        return (viewer, friend, await LiveStream.OpenAsync(_client, viewer));
    }

    /// <summary>A stream request whose answer the test inspects itself — <see cref="LiveStream.OpenAsync"/> insists on 200.</summary>
    private async Task<HttpResponseMessage> OpenRawAsync(LoggedInUser user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/now-playing/stream");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
    }

    /// <summary>The message as any instance sends it on the channel — the contract between instances.</summary>
    private Task PublishFromAnotherInstanceAsync(LoggedInUser subject, string title) =>
        factory.Services.GetRequiredService<IConnectionMultiplexer>().GetSubscriber().PublishAsync(
            RedisChannel.Literal("now-playing:status-changed"),
            JsonSerializer.Serialize(new { UserId = subject.Id, Status = new { Provider = "spotify", Title = title, Artist = "A", Album = "B", ImageUrl = (string?)null, Playing = true, FetchedAt = DateTimeOffset.UtcNow } }));

    private async Task SetManualAsync(LoggedInUser user, string title) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title, artist = "Queen", source = "Tidal" }, user.Token)).StatusCode);

    private static string NameOf(LoggedInUser user) => user.User.Email[..user.User.Email.IndexOf('@', StringComparison.Ordinal)];

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 10 seconds");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
