using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Profile;
using StackExchange.Redis;

namespace Songly.IntegrationTests.NowPlaying;

/// <summary>
/// Connecting Spotify through OAuth and reading the status from it, against a fake Spotify. Ported
/// from the original <c>SpotifyErrorHandlingIntegrationTest</c>, <c>SpotifyClientTest</c>,
/// <c>SpotifyProviderTest</c> and the OAuth scenarios of <c>NowPlayingIntegrationTest</c>.
/// </summary>
public sealed class SpotifyTests : IDisposable
{
    private readonly SonglyApiFactory _factory;
    private readonly FakeSpotify _spotify = new();
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;

    public SpotifyTests(SonglyApiFactory factory)
    {
        _factory = factory;
        _app = _spotify.Attach(factory);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task Connecting_gives_Spotifys_authorize_url_with_a_state()
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, "spotify-connect");

        var url = new Uri(await AuthorizeUrlAsync(user));

        Assert.Equal("accounts.spotify.com", url.Host);
        Assert.Equal("/authorize", url.AbsolutePath);
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("user-read-currently-playing", query["scope"]);
        Assert.Equal("http://localhost:8080/api/v1/now-playing/spotify/callback", query["redirect_uri"]);
        Assert.True(query["state"].ToString().Length >= 32);
    }

    /// <summary>The whole round trip: the callback exchanges the code with Basic auth and a form, and keeps the tokens only encrypted.</summary>
    [Fact]
    public async Task The_callback_connects_the_account_and_keeps_the_tokens_encrypted()
    {
        var user = await ConnectAsync("spotify-tokens");

        var exchange = Assert.Single(_spotify.Requests, r => r.Path == "/api/token");
        Assert.Equal(HttpMethod.Post, exchange.Method);
        Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("client-id:client-secret"))}", exchange.Authorization);
        var form = QueryHelpers.ParseQuery(exchange.Body);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("auth-code", form["code"]);
        Assert.Equal("http://localhost:8080/api/v1/now-playing/spotify/callback", form["redirect_uri"]);

        var stored = await _factory.QuerySingleAsync<string>("SELECT access_token_encrypted || ' ' || refresh_token_encrypted FROM music_provider_links WHERE user_id = @id AND provider = 'spotify'", ("id", user.Id));
        Assert.DoesNotContain("access", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh", stored, StringComparison.Ordinal);
    }

    /// <summary>The status comes from the provider once, then from the cache — reads must not turn into calls to Spotify's API and its rate limits.</summary>
    [Fact]
    public async Task The_status_comes_from_Spotify_and_is_cached()
    {
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":3600}""");
        var user = await ConnectAsync("spotify-playing");
        _spotify.Api = () => FakeSpotify.Playing("Paranoid Android", "Radiohead, Thom Yorke", "OK Computer", "https://i.scdn.co/large");

        var status = await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadJsonAsync();

        Assert.Equal("spotify", status.GetProperty("provider").GetString());
        Assert.Equal("Paranoid Android", status.GetProperty("title").GetString());
        Assert.Equal("Radiohead, Thom Yorke", status.GetProperty("artist").GetString());
        Assert.Equal("OK Computer", status.GetProperty("album").GetString());
        Assert.Equal("https://i.scdn.co/large", status.GetProperty("imageUrl").GetString());
        Assert.True(status.GetProperty("playing").GetBoolean());
        var call = Assert.Single(_spotify.Requests, r => r.Path == "/v1/me/player/currently-playing");
        Assert.Equal("Bearer access", call.Authorization);

        await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadJsonAsync();
        Assert.Equal(1, _spotify.ApiCalls);
    }

    /// <summary>"Nothing playing" (Spotify's 204) is remembered too — a silent user is not asked again on every read.</summary>
    [Fact]
    public async Task Nothing_playing_is_cached_as_well()
    {
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":3600}""");
        var user = await ConnectAsync("spotify-silent");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);

        Assert.Equal(1, _spotify.ApiCalls);
    }

    /// <summary>A manual status takes precedence, so Spotify is not even asked.</summary>
    [Fact]
    public async Task A_manual_status_takes_precedence_without_asking_Spotify()
    {
        var user = await ConnectAsync("spotify-manual");
        await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "Ręcznie", source = "Tidal" }, user.Token);

        Assert.Equal("Ręcznie", (await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadJsonAsync()).GetProperty("title").GetString());
        Assert.Equal(0, _spotify.ApiCalls);
    }

    /// <summary>
    /// An expired access token is refreshed with the refresh token, and the new tokens are saved.
    /// Spotify usually returns no new refresh token: the old one is kept for the next refresh.
    /// </summary>
    [Fact]
    public async Task An_expired_token_is_refreshed_and_the_refresh_token_kept()
    {
        var user = await ConnectAsync("spotify-refresh");
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"fresh-access","expires_in":0}""");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);

        Assert.Equal("refresh_token", QueryHelpers.ParseQuery(_spotify.Requests.Last(r => r.Path == "/api/token").Body)["grant_type"]);
        Assert.Equal("Bearer fresh-access", _spotify.Requests.Last().Authorization);

        // The refreshed token expired at once (expires_in=0), so the next read refreshes again — with the kept refresh token.
        await ForgetCachedStatusAsync(user);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);
        Assert.Equal("refresh", QueryHelpers.ParseQuery(_spotify.Requests.Last(r => r.Path == "/api/token").Body)["refresh_token"]);
    }

    /// <summary>
    /// The link is checked before the provider, and without the cache: a "nothing playing" cached for an
    /// account with no service would hide a freshly connected Spotify for the whole lifetime of the entry.
    /// </summary>
    [Fact]
    public async Task A_read_before_connecting_does_not_hide_the_new_connection()
    {
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":3600}""");
        var user = await _factory.CreateLoggedInUserAsync(_client, "spotify-late-connect");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);

        Assert.Equal("songly://now-playing/spotify?result=connected", await CallbackAsync($"code=auth-code&state={StateOf(await AuthorizeUrlAsync(user))}"));
        _spotify.Api = () => FakeSpotify.Playing("Utwór", "Artysta", "Album", "https://i.scdn.co/x");

        Assert.Equal("Utwór", (await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadJsonAsync()).GetProperty("title").GetString());
    }

    /// <summary>
    /// Consent taken back in Spotify: 409, and the dead link is gone. Kept, the poll would knock on
    /// Spotify with the same dead token every 30 seconds for ever.
    /// </summary>
    [Fact]
    public async Task Revoked_consent_is_a_conflict_and_drops_the_dead_link()
    {
        var user = await ConnectAsync("spotify-revoked");
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Refresh token revoked"}""");

        await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadErrorAsync(HttpStatusCode.Conflict, "PROVIDER_REAUTHORIZATION_REQUIRED");

        Assert.False(await LinkedAsync(user));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);
    }

    /// <summary>Someone else's outage is a 502, not our 500 — and it does not cost the user their link.</summary>
    [Fact]
    public async Task A_provider_failure_is_a_bad_gateway_and_keeps_the_link()
    {
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":3600}""");
        var user = await ConnectAsync("spotify-outage");
        _spotify.Api = () => FakeSpotify.Json(HttpStatusCode.ServiceUnavailable, "{}");

        await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadErrorAsync(HttpStatusCode.BadGateway, "MUSIC_PROVIDER_ERROR");

        Assert.True(await LinkedAsync(user));
    }

    /// <summary>An error body that is not JSON must not hide the HTTP error itself.</summary>
    [Fact]
    public async Task A_token_error_without_json_is_still_a_bad_gateway()
    {
        var user = await ConnectAsync("spotify-html-error");
        _spotify.Token = () => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("<html>oops</html>") };

        await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadErrorAsync(HttpStatusCode.BadGateway, "MUSIC_PROVIDER_ERROR");
        Assert.True(await LinkedAsync(user));
    }

    /// <summary>
    /// A token refreshed just before a failing API call is real and must be kept (original ADR 0049).
    /// Lost, with a rotated refresh token the stale one stayed in the database, and the next refresh
    /// could delete a healthy link.
    /// </summary>
    [Fact]
    public async Task A_token_refreshed_before_a_provider_failure_is_kept()
    {
        var user = await ConnectAsync("spotify-refresh-kept");
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"fresh-access","refresh_token":"rotated-refresh","expires_in":3600}""");
        _spotify.Api = () => FakeSpotify.Json(HttpStatusCode.ServiceUnavailable, "{}");

        Assert.Equal(HttpStatusCode.BadGateway, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);

        // The next read goes straight to the API with the saved token — no second refresh.
        var refreshes = _spotify.Requests.Count(r => r.Path == "/api/token");
        _spotify.Api = () => new HttpResponseMessage(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);
        Assert.Equal(refreshes, _spotify.Requests.Count(r => r.Path == "/api/token"));
        Assert.Equal("Bearer fresh-access", _spotify.Requests.Last().Authorization);
    }

    [Fact]
    public async Task A_stale_authorization_code_returns_to_the_app_with_its_error()
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, "spotify-stale-code");
        var state = StateOf(await AuthorizeUrlAsync(user));
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Invalid authorization code"}""");

        Assert.Equal("songly://now-playing/spotify?result=error&code=INVALID_AUTHORIZATION_CODE", await CallbackAsync($"code=stale&state={state}"));
        Assert.False(await LinkedAsync(user));
    }

    [Fact]
    public async Task A_provider_outage_during_the_exchange_returns_to_the_app()
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, "spotify-exchange-outage");
        var state = StateOf(await AuthorizeUrlAsync(user));
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.ServiceUnavailable, "{}");

        Assert.Equal("songly://now-playing/spotify?result=error&code=MUSIC_PROVIDER_ERROR", await CallbackAsync($"code=auth-code&state={state}"));
    }

    /// <summary>On the other side of the callback is a browser, not an API client: an error goes back to the app, not to a page of JSON.</summary>
    [Fact]
    public async Task An_unknown_state_returns_to_the_app_with_an_error()
    {
        Assert.Equal("songly://now-playing/spotify?result=error&code=INVALID_OAUTH_STATE", await CallbackAsync("code=any-code&state=unknown-state"));
    }

    /// <summary>A state works once: replaying the callback cannot connect a second time.</summary>
    [Fact]
    public async Task A_state_is_used_up_by_its_callback()
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, "spotify-replay");
        var state = StateOf(await AuthorizeUrlAsync(user));
        Assert.Equal("songly://now-playing/spotify?result=connected", await CallbackAsync($"code=auth-code&state={state}"));

        Assert.Equal("songly://now-playing/spotify?result=error&code=INVALID_OAUTH_STATE", await CallbackAsync($"code=auth-code&state={state}"));
    }

    /// <summary>Refusing consent is a result of its own, and it uses the state up — putting it back with a code later cannot finish the connection.</summary>
    [Fact]
    public async Task Refusing_consent_returns_as_denied_and_uses_the_state_up()
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, "spotify-denied");
        var state = StateOf(await AuthorizeUrlAsync(user));

        Assert.Equal("songly://now-playing/spotify?result=denied", await CallbackAsync($"error=access_denied&state={state}"));
        Assert.Equal("songly://now-playing/spotify?result=error&code=INVALID_OAUTH_STATE", await CallbackAsync($"code=late-code&state={state}"));
        Assert.DoesNotContain(_spotify.Requests, r => r.Path == "/api/token");
    }

    [Fact]
    public async Task Another_provider_error_returns_as_a_provider_error()
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, "spotify-provider-error");
        var state = StateOf(await AuthorizeUrlAsync(user));

        Assert.Equal("songly://now-playing/spotify?result=error&code=MUSIC_PROVIDER_ERROR", await CallbackAsync($"error=server_error&state={state}"));
    }

    /// <summary>Without a state it is no return from the provider — the one case that answers with JSON.</summary>
    [Fact]
    public async Task A_callback_without_a_state_is_a_bad_request()
    {
        var response = await _client.GetAsync("/api/v1/now-playing/spotify/callback?code=x", accessToken: null);

        await response.ReadErrorAsync(HttpStatusCode.BadRequest, "MISSING_PARAMETER");
    }

    /// <summary>Disconnecting is idempotent, forgets the cached status, and leaves a manual one alone.</summary>
    [Fact]
    public async Task Disconnecting_drops_the_link_and_the_cached_status_but_not_the_manual_one()
    {
        _spotify.Token = () => FakeSpotify.Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":3600}""");
        var user = await ConnectAsync("spotify-disconnect");
        _spotify.Api = () => FakeSpotify.Playing("Utwór", "Artysta", "Album", "https://i.scdn.co/x");
        await (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/v1/now-playing/spotify", user.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/v1/now-playing/spotify", user.Token)).StatusCode);

        Assert.False(await LinkedAsync(user));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync("/api/v1/now-playing/me", user.Token)).StatusCode);

        var manual = await ConnectAsync("spotify-disconnect-manual");
        await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "Ręcznie" }, manual.Token);
        await _client.DeleteAsync("/api/v1/now-playing/spotify", manual.Token);
        Assert.Equal("Ręcznie", (await (await _client.GetAsync("/api/v1/now-playing/me", manual.Token)).ReadJsonAsync()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Deleting_the_account_removes_the_link()
    {
        var user = await ConnectAsync("spotify-deleted");

        Assert.Equal(HttpStatusCode.NoContent, (await new CommunityTestClient(_factory, _client).DeleteAccountAsync(user)).StatusCode);

        Assert.False(await LinkedAsync(user));
    }

    [Fact]
    public async Task Connecting_and_disconnecting_require_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostJsonAsync("/api/v1/now-playing/spotify/connect", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.DeleteAsync("/api/v1/now-playing/spotify", accessToken: null)).StatusCode);
    }

    /// <summary>Registration and the full OAuth round trip through the public callback.</summary>
    private async Task<LoggedInUser> ConnectAsync(string prefix)
    {
        var user = await _factory.CreateLoggedInUserAsync(_client, prefix);
        var state = StateOf(await AuthorizeUrlAsync(user));
        Assert.Equal("songly://now-playing/spotify?result=connected", await CallbackAsync($"code=auth-code&state={state}"));
        Assert.True(await LinkedAsync(user));
        return user;
    }

    private async Task<string> AuthorizeUrlAsync(LoggedInUser user) =>
        (await (await _client.PostJsonAsync("/api/v1/now-playing/spotify/connect", new { }, user.Token)).ReadJsonAsync()).GetProperty("authorizeUrl").GetString()!;

    private static string StateOf(string authorizeUrl) => QueryHelpers.ParseQuery(new Uri(authorizeUrl).Query)["state"].ToString();

    /// <summary>The callback as a browser follows it: no token, and the answer is a 302 whose Location is the result.</summary>
    private async Task<string> CallbackAsync(string query)
    {
        using var response = await _client.GetAsync($"/api/v1/now-playing/spotify/callback?{query}", accessToken: null);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }

    /// <summary>Drops the cached answer, as its expiry would — the test cannot wait 45 seconds.</summary>
    private Task ForgetCachedStatusAsync(LoggedInUser user) =>
        _app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyDeleteAsync($"now-playing:status:{user.Id}");

    private async Task<bool> LinkedAsync(LoggedInUser user) =>
        await _factory.QuerySingleAsync<long>("SELECT count(*) FROM music_provider_links WHERE user_id = @id", ("id", user.Id)) == 1;
}
