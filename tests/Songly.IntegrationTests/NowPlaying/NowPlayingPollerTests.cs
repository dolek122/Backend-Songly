using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;
using StackExchange.Redis;

namespace Songly.IntegrationTests.NowPlaying;

/// <summary>
/// The server-side poll and the heartbeat, running in an instance of their own against a fake Spotify.
/// Ported from the original <c>NowPlayingExpiredStatusIntegrationTest</c>, <c>NowPlayingPollerTest</c>
/// and the refresh scenarios of <c>NowPlayingServiceRefreshTest</c>.
/// </summary>
/// <remarks>
/// The poll runs every 300 ms with a lock shorter than that, so every run asks again. It observes only
/// the friends of viewers with a stream on <b>this</b> instance, so the streams are opened here. The
/// backoff key is global to the installation; only this class sets it, and every test clears it.
/// </remarks>
public sealed class NowPlayingPollerTests : IAsyncLifetime
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(2);

    private readonly SonglyApiFactory _factory;
    private readonly FakeSpotify _spotify = new();
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;
    private readonly ContentTestClient _social;

    public NowPlayingPollerTests(SonglyApiFactory factory)
    {
        _factory = factory;
        _app = _spotify.Attach(
            factory,
            ("Songly:NowPlaying:Poll:Enabled", "true"),
            ("Songly:NowPlaying:Poll:Interval", "00:00:00.300"),
            ("Songly:NowPlaying:Poll:LockTtl", "00:00:00.100"),
            ("Songly:NowPlaying:Stream:HeartbeatEnabled", "true"),
            ("Songly:NowPlaying:Stream:HeartbeatInterval", "00:00:00.200"));
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _social = new ContentTestClient(_client);
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":3600}""");
    }

    private IDatabase Redis => _app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    public async ValueTask InitializeAsync() => await Redis.KeyDeleteAsync("now-playing:provider-backoff");

    public async ValueTask DisposeAsync()
    {
        await Redis.KeyDeleteAsync("now-playing:provider-backoff");
        _client.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// A manual status that expired reaches a watching friend as "stopped listening". Nothing else would
    /// notice: its owner has no provider to poll, and Redis does not announce expired keys.
    /// </summary>
    [Fact]
    public async Task An_expired_manual_status_pushes_stopped_to_a_watching_friend()
    {
        var (_, subject, stream) = await WatchingAsync("poll-expiry");
        await using var _ = stream;
        await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "Bohemian Rhapsody" }, subject.Token);
        await stream.NextAsync();

        // The lifetime running out, without waiting three hours: to the poll, a deleted key looks the same.
        await Redis.KeyDeleteAsync($"now-playing:manual:{subject.Id}");

        var push = await stream.NextAsync();
        Assert.Equal(subject.Id, push.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.Null, push.GetProperty("status").ValueKind);
        Assert.True(await stream.NothingWithinAsync(Quiet));
    }

    /// <summary>A status still alive produces no further pushes, however many runs go by.</summary>
    [Fact]
    public async Task A_live_status_stays_quiet()
    {
        var (_, subject, stream) = await WatchingAsync("poll-quiet");
        await using var _ = stream;
        await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "Come As You Are" }, subject.Token);
        await stream.NextAsync();

        Assert.True(await stream.NothingWithinAsync(Quiet));
    }

    /// <summary>
    /// The poll fills the cache from Spotify and pushes only a real change of track — not every run —
    /// then "stopped listening" when playback ends.
    /// </summary>
    [Fact]
    public async Task The_poll_pushes_changes_of_track_once_and_the_end_of_playback()
    {
        var (_, subject, stream) = await WatchingAsync("poll-track");
        await using var _ = stream;
        _spotify.Api = () => FakeSpotify.Playing("Pierwszy", "Artysta", "Album", "https://i.scdn.co/1");
        await ConnectAsync(subject);

        Assert.Equal("Pierwszy", (await stream.NextAsync()).GetProperty("status").GetProperty("title").GetString());
        Assert.True(await stream.NothingWithinAsync(Quiet));
        Assert.True(_spotify.ApiCalls > 2, "the poll keeps asking");

        _spotify.Api = () => FakeSpotify.Playing("Drugi", "Artysta", "Album", "https://i.scdn.co/2");
        Assert.Equal("Drugi", (await stream.NextAsync()).GetProperty("status").GetProperty("title").GetString());

        _spotify.Api = () => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(JsonValueKind.Null, (await stream.NextAsync()).GetProperty("status").ValueKind);
        Assert.True(await stream.NothingWithinAsync(Quiet));
    }

    /// <summary>Nobody watching, nobody asked: a connected user without a viewer is not polled.</summary>
    [Fact]
    public async Task Nobody_watching_means_nobody_is_asked()
    {
        var lonely = await _factory.CreateLoggedInUserAsync(_client, "poll-lonely");
        await ConnectAsync(lonely);

        await Task.Delay(Quiet, TestContext.Current.CancellationToken);

        Assert.Equal(0, _spotify.ApiCalls);
    }

    /// <summary>Whoever hid their status is not asked about — it would not leave the server anyway.</summary>
    [Fact]
    public async Task A_hidden_user_is_not_polled()
    {
        var (_, subject, stream) = await WatchingAsync("poll-hidden");
        await using var _ = stream;
        await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = false }, subject.Token);
        await stream.NextAsync();
        await ConnectAsync(subject);

        await Task.Delay(Quiet, TestContext.Current.CancellationToken);

        Assert.Equal(0, _spotify.ApiCalls);
    }

    /// <summary>
    /// A 429 stops the run and silences the poll for Spotify's <c>Retry-After</c> — on every instance, since
    /// the limit is per client ID — instead of knocking again and deepening the penalty.
    /// </summary>
    [Fact]
    public async Task A_rate_limit_backs_off_for_retry_after()
    {
        var (_, subject, stream) = await WatchingAsync("poll-rate-limit");
        await using var _ = stream;
        _spotify.Api = () =>
        {
            var response = FakeSpotify.Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.Add("Retry-After", "120");
            return response;
        };
        await ConnectAsync(subject);

        await WaitUntilAsync(() => Redis.KeyExistsAsync("now-playing:provider-backoff"));
        var ttl = await Redis.KeyTimeToLiveAsync("now-playing:provider-backoff");
        Assert.InRange(ttl!.Value, TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(120));

        var calls = _spotify.ApiCalls;
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);
        Assert.Equal(calls, _spotify.ApiCalls);
    }

    /// <summary>Disconnecting Spotify tells watching friends "stopped listening" — unless a manual status is still playing.</summary>
    [Fact]
    public async Task Disconnecting_pushes_stopped()
    {
        var (_, subject, stream) = await WatchingAsync("poll-disconnect");
        await using var _ = stream;
        _spotify.Api = () => FakeSpotify.Playing("Utwór", "Artysta", "Album", "https://i.scdn.co/x");
        await ConnectAsync(subject);
        await stream.NextAsync();

        await _client.DeleteAsync("/api/v1/now-playing/spotify", subject.Token);

        Assert.Equal(JsonValueKind.Null, (await stream.NextAsync()).GetProperty("status").ValueKind);
    }

    /// <summary>The heartbeat keeps a silent stream alive with comments, which clients ignore.</summary>
    [Fact]
    public async Task A_silent_stream_gets_heartbeats()
    {
        var viewer = await _factory.CreateLoggedInUserAsync(_client, "poll-heartbeat");
        await using var stream = await LiveStream.OpenAsync(_client, viewer);

        await WaitUntilAsync(() => Task.FromResult(stream.Comments >= 2));
        Assert.True(await stream.NothingWithinAsync(TimeSpan.FromMilliseconds(500)));
    }

    private async Task<(LoggedInUser Viewer, LoggedInUser Subject, LiveStream Stream)> WatchingAsync(string prefix)
    {
        var viewer = await _factory.CreateLoggedInUserAsync(_client, $"{prefix}-viewer");
        var subject = await _factory.CreateLoggedInUserAsync(_client, $"{prefix}-subject");
        await _social.BefriendAsync(viewer, subject);
        return (viewer, subject, await LiveStream.OpenAsync(_client, viewer));
    }

    private async Task ConnectAsync(LoggedInUser user)
    {
        var authorize = (await (await _client.PostJsonAsync("/api/v1/now-playing/spotify/connect", new { }, user.Token)).ReadJsonAsync()).GetProperty("authorizeUrl").GetString()!;
        var state = QueryHelpers.ParseQuery(new Uri(authorize).Query)["state"];
        using var callback = await _client.GetAsync($"/api/v1/now-playing/spotify/callback?code=auth-code&state={state}", accessToken: null);
        Assert.Equal("songly://now-playing/spotify?result=connected", callback.Headers.Location?.ToString());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 10 seconds");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
