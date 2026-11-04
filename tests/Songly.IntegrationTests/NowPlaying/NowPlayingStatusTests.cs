using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;
using StackExchange.Redis;

namespace Songly.IntegrationTests.NowPlaying;

/// <summary>
/// The own status, the manual fallback, the friends' statuses and the visibility setting. Ported from
/// the original <c>NowPlayingIntegrationTest</c> (without the Spotify and live-stream scenarios, which
/// come with those parts).
/// </summary>
public sealed class NowPlayingStatusTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _social = new(factory.CreateClient());

    private IDatabase Redis => factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    /// <summary>
    /// Nothing connected and nothing set by hand — every new account — is "nothing playing" (204), not
    /// an error: an undocumented 404 here once made the mobile app cover the screen, the manual form included.
    /// </summary>
    [Fact]
    public async Task A_new_account_plays_nothing()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-status");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);
    }

    [Fact]
    public async Task A_manual_status_is_shown_and_can_be_cleared()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-manual");

        await SetManualAsync(user, "Bohemian Rhapsody", "Queen", "Tidal");

        var status = await MineAsync(user);
        Assert.Equal("Tidal", status.GetProperty("provider").GetString());
        Assert.Equal("Bohemian Rhapsody", status.GetProperty("title").GetString());
        Assert.Equal("Queen", status.GetProperty("artist").GetString());
        Assert.True(status.GetProperty("playing").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("imageUrl").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/v1/now-playing/manual", user.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);
    }

    [Fact]
    public async Task A_manual_status_without_a_source_is_labelled_manual()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-manual-default");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "Nowa piosenka" }, user.Token)).StatusCode);

        Assert.Equal("manual", (await MineAsync(user)).GetProperty("provider").GetString());
    }

    [Fact]
    public async Task A_manual_status_needs_a_title_and_short_fields()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-manual-invalid");

        await (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "" }, user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "x", source = new string('s', 61) }, user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = new string('t', 201) }, user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Nothing refreshes a manual status, so it must expire on its own — hours, not for ever.</summary>
    [Fact]
    public async Task A_manual_status_expires()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-manual-ttl");

        await SetManualAsync(user, "Utwór", "Artysta", "Tidal");

        var ttl = await Redis.KeyTimeToLiveAsync($"now-playing:manual:{user.Id}");
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromHours(2.9), TimeSpan.FromHours(3));
    }

    /// <summary>
    /// The manual status takes precedence over the provider's until it is cleared (original Claude.md §7),
    /// on the single read and on the friends list alike. The provider's entry is written straight into
    /// Redis: until the Spotify integration arrives, the API cannot produce it.
    /// </summary>
    [Fact]
    public async Task A_manual_status_takes_precedence_over_the_providers()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "np-precedence-viewer");
        var user = await factory.CreateLoggedInUserAsync(_client, "np-precedence");
        await _social.BefriendAsync(viewer, user);
        await ProviderStatusAsync(user, JsonSerializer.Serialize(new { Provider = "spotify", Title = "Z providera", Artist = "A", Album = "B", ImageUrl = "https://i.scdn.co/x", Playing = true, FetchedAt = DateTimeOffset.UtcNow }));
        Assert.Equal("Z providera", (await MineAsync(user)).GetProperty("title").GetString());

        await SetManualAsync(user, "Ręcznie", "Artysta", "Tidal");

        Assert.Equal("Ręcznie", (await MineAsync(user)).GetProperty("title").GetString());
        Assert.Equal("Ręcznie", (await FriendsAsync(viewer))[0].GetProperty("status").GetProperty("title").GetString());

        await _client.DeleteAsync("/api/v1/now-playing/manual", user.Token);

        var status = await MineAsync(user);
        Assert.Equal("Z providera", status.GetProperty("title").GetString());
        Assert.Equal("spotify", status.GetProperty("provider").GetString());
        Assert.Equal("https://i.scdn.co/x", status.GetProperty("imageUrl").GetString());
        Assert.Equal("Z providera", (await FriendsAsync(viewer))[0].GetProperty("status").GetProperty("title").GetString());
    }

    /// <summary>The provider's "nothing is playing" is remembered too — and reads as nothing, not as a status.</summary>
    [Fact]
    public async Task The_providers_nothing_playing_reads_as_nothing()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "np-nothing-viewer");
        var user = await factory.CreateLoggedInUserAsync(_client, "np-nothing");
        await _social.BefriendAsync(viewer, user);

        await ProviderStatusAsync(user, "NOTHING_PLAYING");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);
        Assert.Equal(0, (await FriendsAsync(viewer)).GetArrayLength());
    }

    [Fact]
    public async Task Friends_with_a_status_are_listed_with_their_profile_data()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "np-friends-viewer");
        var playing = await factory.CreateLoggedInUserAsync(_client, "np-friends-playing");
        var silent = await factory.CreateLoggedInUserAsync(_client, "np-friends-silent");
        var stranger = await factory.CreateLoggedInUserAsync(_client, "np-friends-stranger");
        await SetManualAsync(playing, "Bohemian Rhapsody", "Queen", "Tidal");
        await SetManualAsync(stranger, "Come As You Are", "Nirvana", "Tidal");
        await _social.BefriendAsync(viewer, playing);
        await _social.BefriendAsync(viewer, silent);

        var friends = await FriendsAsync(viewer);

        var friend = Assert.Single(friends.EnumerateArray());
        Assert.Equal(playing.Id, friend.GetProperty("userId").GetGuid());
        Assert.Equal(NameOf(playing), friend.GetProperty("displayName").GetString());
        Assert.Equal("Tidal", friend.GetProperty("status").GetProperty("provider").GetString());
        Assert.Equal("Bohemian Rhapsody", friend.GetProperty("status").GetProperty("title").GetString());
        Assert.Equal("Queen", friend.GetProperty("status").GetProperty("artist").GetString());
        Assert.True(friend.GetProperty("status").GetProperty("playing").GetBoolean());
    }

    [Fact]
    public async Task Friends_are_sorted_by_name()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "np-sort-viewer");
        var later = await factory.CreateLoggedInUserAsync(_client, "np-sort-zz");
        var earlier = await factory.CreateLoggedInUserAsync(_client, "np-sort-aa");
        foreach (var friend in new[] { later, earlier })
        {
            await _social.BefriendAsync(viewer, friend);
            await SetManualAsync(friend, "Utwór", "Artysta", "Tidal");
        }

        Assert.Equal([earlier.Id, later.Id], (await FriendsAsync(viewer)).EnumerateArray().Select(f => f.GetProperty("userId").GetGuid()));
    }

    [Fact]
    public async Task No_friend_playing_is_an_empty_list()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "np-friends-empty-viewer");
        await _social.BefriendAsync(viewer, await factory.CreateLoggedInUserAsync(_client, "np-friends-empty-friend"));

        Assert.Equal(0, (await FriendsAsync(viewer)).GetArrayLength());
        Assert.Equal(0, (await FriendsAsync(await factory.CreateLoggedInUserAsync(_client, "np-friendless"))).GetArrayLength());
    }

    /// <summary>
    /// A suspended account's status does not leave the server (original ADR 0049) — a manual status
    /// lives for hours, so "a suspended user will not refresh it" was not enough.
    /// </summary>
    [Fact]
    public async Task A_suspended_friend_is_left_out()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "np-suspended-viewer");
        var suspended = await factory.CreateLoggedInUserAsync(_client, "np-suspended");
        await SetManualAsync(suspended, "Bohemian Rhapsody", "Queen", "Tidal");
        await _social.BefriendAsync(viewer, suspended);
        Assert.Equal(1, (await FriendsAsync(viewer)).GetArrayLength());

        await factory.SuspendAsync(suspended.User);

        Assert.Equal(0, (await FriendsAsync(viewer)).GetArrayLength());
    }

    [Fact]
    public async Task Sharing_is_on_by_default_and_can_be_turned_off()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-settings");
        Assert.True((await SettingsAsync(user)).GetProperty("shareWithFriends").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = false }, user.Token)).StatusCode);
        Assert.False((await SettingsAsync(user)).GetProperty("shareWithFriends").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = true }, user.Token)).StatusCode);
        Assert.True((await SettingsAsync(user)).GetProperty("shareWithFriends").GetBoolean());
    }

    [Fact]
    public async Task The_setting_needs_its_flag()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-settings-invalid");

        await (await _client.PutJsonAsync("/api/v1/now-playing/settings", new { }, user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Turning sharing off hides the status from friends (original ADR 0007), but the owner still sees their own.</summary>
    [Fact]
    public async Task A_hidden_status_leaves_the_friends_list_but_stays_visible_to_its_owner()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "np-hidden-viewer");
        var hidden = await factory.CreateLoggedInUserAsync(_client, "np-hidden-subject");
        await _social.BefriendAsync(viewer, hidden);
        await SetManualAsync(hidden, "Bohemian Rhapsody", "Queen", "Tidal");
        Assert.Equal(1, (await FriendsAsync(viewer)).GetArrayLength());

        await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = false }, hidden.Token);

        Assert.Equal(0, (await FriendsAsync(viewer)).GetArrayLength());
        Assert.Equal("Bohemian Rhapsody", (await MineAsync(hidden)).GetProperty("title").GetString());

        // Turning it back on needs no new status: the stored one shows again.
        await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = true }, hidden.Token);
        Assert.Equal(1, (await FriendsAsync(viewer)).GetArrayLength());
    }

    /// <summary>Account deletion takes the preference row with it, and the status in Redis after the commit.</summary>
    [Fact]
    public async Task Deleting_the_account_removes_the_setting_and_the_status()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "np-deleted");
        await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = false }, user.Token);
        await SetManualAsync(user, "Utwór", "Artysta", "Tidal");

        Assert.Equal(HttpStatusCode.NoContent, (await new CommunityTestClient(factory, _client).DeleteAccountAsync(user)).StatusCode);

        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM now_playing_preferences WHERE user_id = @id", ("id", user.Id)));
        Assert.False(await Redis.KeyExistsAsync($"now-playing:manual:{user.Id}"));
    }

    [Fact]
    public async Task The_endpoints_require_authentication()
    {
        foreach (var path in new[] { "/api/v1/now-playing/me", "/api/v1/now-playing/friends", "/api/v1/now-playing/settings" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(path, accessToken: null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.DeleteAsync("/api/v1/now-playing/manual", accessToken: null)).StatusCode);
    }

    private Task ProviderStatusAsync(LoggedInUser user, string raw) =>
        Redis.StringSetAsync($"now-playing:status:{user.Id}", raw, TimeSpan.FromMinutes(5));

    private async Task SetManualAsync(LoggedInUser user, string title, string artist, string source) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title, artist, source }, user.Token)).StatusCode);

    private async Task<JsonElement> MineAsync(LoggedInUser user) =>
        await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadJsonAsync();

    private async Task<JsonElement> FriendsAsync(LoggedInUser user) =>
        await (await _client.GetAsync("/api/v1/now-playing/friends", user.Token)).ReadJsonAsync();

    private async Task<JsonElement> SettingsAsync(LoggedInUser user) =>
        await (await _client.GetAsync("/api/v1/now-playing/settings", user.Token)).ReadJsonAsync();

    private static string NameOf(LoggedInUser user) => user.User.Email[..user.User.Email.IndexOf('@', StringComparison.Ordinal)];
}
