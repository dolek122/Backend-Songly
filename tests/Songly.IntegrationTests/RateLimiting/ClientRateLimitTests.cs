using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using StackExchange.Redis;

namespace Songly.IntegrationTests.RateLimiting;

/// <summary>
/// The limit per client address on the public paths (original Claude.md §9), in an instance with a limit of 2.
/// Ported from the original <c>RateLimitingIntegrationTest</c> and <c>RateLimitingFilterTest</c>.
/// </summary>
/// <remarks>
/// The instance runs as behind a proxy, so every test sends its own <c>X-Forwarded-For</c> address: the windows
/// live in the Redis every test shares, and one address per test keeps them apart.
/// </remarks>
public sealed class ClientRateLimitTests : IDisposable
{
    private readonly SonglyApiFactory _factory;
    private readonly WebApplicationFactory<Program> _app;
    private readonly HttpClient _client;

    public ClientRateLimitTests(SonglyApiFactory factory)
    {
        _factory = factory;
        _app = factory.WithSettings(
            ("Songly:Security:RateLimit:Enabled", "true"),
            ("Songly:Security:RateLimit:Limit", "2"),
            ("Songly:Security:RateLimit:Window", "00:01:00"),
            ("Songly:ForwardedHeaders:Enabled", "true"));
        _client = _app.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task Registration_is_refused_over_the_limit_with_retry_after()
    {
        var address = NewAddress();
        using var rejections = new MetricCollector<long>(_app.Services.GetRequiredService<IMeterFactory>(), "Songly.RateLimiting", "songly.rate.limit.rejected");

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(address, "/api/v1/auth/register", new { email = TestAccounts.UniqueEmail("ratelimit"), password = TestAccounts.DefaultPassword })).StatusCode);
        }

        var refused = await PostAsync(address, "/api/v1/auth/register", new { email = TestAccounts.UniqueEmail("ratelimit"), password = TestAccounts.DefaultPassword });

        var error = await refused.ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
        Assert.Equal("Zbyt wiele żądań, spróbuj ponownie później", error.GetProperty("message").GetString());

        // From the window's real remaining time — the client knows when to come back instead of hammering.
        var retryAfter = Assert.Single(refused.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture), 1, 60);

        // Tagged by the pattern, never the request's own path.
        var rejection = Assert.Single(rejections.GetMeasurementSnapshot());
        Assert.Equal("/api/v1/auth/register", rejection.Tags["path"]);
    }

    /// <summary><c>/refresh</c> is public, so without the limit refresh tokens could be guessed forever.</summary>
    [Fact]
    public async Task Refresh_is_limited_too()
    {
        var address = NewAddress();
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(address, "/api/v1/auth/refresh", new { refreshToken = "nie-istnieje" })).StatusCode);
        }

        await (await PostAsync(address, "/api/v1/auth/refresh", new { refreshToken = "nie-istnieje" })).ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
    }

    /// <summary>The windows are per path, so using up one public path must not block another.</summary>
    [Fact]
    public async Task A_limit_on_one_path_does_not_block_another()
    {
        var address = NewAddress();
        for (var i = 0; i < 3; i++)
        {
            await PostAsync(address, "/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword });
        }

        // Logout is idempotent, so an unknown token is an ordinary 204 — anything but a 429 inherited from next door.
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(address, "/api/v1/auth/logout", new { refreshToken = "nie-istnieje" })).StatusCode);
    }

    /// <summary>The window belongs to the address: another client behind the same proxy has a budget of its own.</summary>
    [Fact]
    public async Task Another_client_address_has_its_own_window()
    {
        var address = NewAddress();
        for (var i = 0; i < 3; i++)
        {
            await PostAsync(address, "/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword });
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(NewAddress(), "/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword })).StatusCode);
    }

    /// <summary>
    /// Only the address the proxy appended counts. Whatever the client put in front of it is a field it controls,
    /// so changing it must not buy a fresh window.
    /// </summary>
    [Fact]
    public async Task Addresses_the_client_puts_in_front_of_the_proxys_do_not_buy_a_new_window()
    {
        var address = NewAddress();
        for (var i = 0; i < 2; i++)
        {
            await PostAsync($"{NewAddress()}, {address}", "/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword });
        }

        await (await PostAsync($"{NewAddress()}, {address}", "/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword }))
            .ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
    }

    /// <summary>
    /// A pattern with a wildcard shares one window across every path it matches — keyed by the request's path, it
    /// would give every id a budget of its own and limit nothing. The limit runs before authentication, so the
    /// anonymous requests count even though they end in 401.
    /// </summary>
    [Fact]
    public async Task A_wildcard_pattern_shares_one_window_across_the_paths_it_matches()
    {
        using var app = _factory.WithSettings(
            ("Songly:Security:RateLimit:Enabled", "true"),
            ("Songly:Security:RateLimit:Limit", "2"),
            ("Songly:Security:RateLimit:ProtectedPaths:0", "/api/v1/profiles/*"),
            ("Songly:ForwardedHeaders:Enabled", "true"));
        using var client = app.CreateClient();
        var address = NewAddress();

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, address, $"/api/v1/profiles/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(client, address, $"/api/v1/profiles/{Guid.NewGuid()}")).StatusCode);
        await (await GetAsync(client, address, $"/api/v1/profiles/{Guid.NewGuid()}")).ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");

        // The configured list replaces the defaults instead of being added to them.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(client, address, "/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword })).StatusCode);
        }
    }

    /// <summary>
    /// <c>INCR</c> and <c>EXPIRE</c> are two commands; a connection dropped between them left a counter without an
    /// expiry, and that address was cut off for good. The refusal gives it its window back.
    /// </summary>
    [Fact]
    public async Task A_window_left_without_an_expiry_gets_one_on_the_next_refusal()
    {
        var address = NewAddress();
        var key = $"rate-limit:/api/v1/auth/login:{address}";
        var redis = _app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await redis.StringSetAsync(key, 100);

        var refused = await PostAsync(address, "/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword });

        await refused.ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
        Assert.Equal("60", Assert.Single(refused.Headers.GetValues("Retry-After")));
        var ttl = await redis.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// A safeguard, not a condition of correctness: with Redis down the requests pass, rather than cutting everyone
    /// off from logging in.
    /// </summary>
    [Fact]
    public async Task Requests_pass_when_redis_does_not_answer()
    {
        using var app = _factory.WithSettings(
            ("Songly:Security:RateLimit:Enabled", "true"),
            ("Songly:Security:RateLimit:Limit", "1"),
            ("ConnectionStrings:Redis", "localhost:1,abortConnect=false"));
        using var client = app.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostJsonAsync("/api/v1/auth/login", new { email = "nie-ma@songly.io", password = TestAccounts.DefaultPassword })).StatusCode);
        }
    }

    /// <summary>
    /// Without a proxy in front, <c>X-Forwarded-For</c> is whatever the client sends — so where forwarding is off,
    /// a new value every time must not buy a new window.
    /// </summary>
    [Fact]
    public async Task Without_forwarding_the_header_is_ignored()
    {
        using var app = _factory.WithSettings(
            ("Songly:Security:RateLimit:Enabled", "true"),
            ("Songly:Security:RateLimit:Limit", "2"),
            ("Songly:Security:RateLimit:ProtectedPaths:0", "/api/v1/auth/password/reset"));
        using var client = app.CreateClient();

        // The only test counting under the test server's own (absent) address, on a path of its own.
        for (var i = 0; i < 2; i++)
        {
            await PostAsync(client, NewAddress(), "/api/v1/auth/password/reset", new { token = "nie-istnieje", newPassword = TestAccounts.DefaultPassword });
        }

        await (await PostAsync(client, NewAddress(), "/api/v1/auth/password/reset", new { token = "nie-istnieje", newPassword = TestAccounts.DefaultPassword }))
            .ReadErrorAsync(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED");
    }

    private Task<HttpResponseMessage> PostAsync(string forwardedFor, string path, object body) => PostAsync(_client, forwardedFor, path, body);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string forwardedFor, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = System.Net.Http.Json.JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string forwardedFor, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string NewAddress() =>
        $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}";
}
