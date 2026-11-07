using System.Net;

namespace Songly.IntegrationTests;

/// <summary>
/// The Prometheus endpoint — the original's <c>/actuator/prometheus</c>, administrators only. Without it the
/// counters recorded across the modules existed only inside the process.
/// </summary>
public sealed class MetricsEndpointTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Administrators_read_the_applications_metrics_in_prometheus_format()
    {
        var admin = await factory.CreateAdminAsync(_client, "metrics-admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync("metrics-nobody@songly.io", "zle-haslo")).StatusCode);

        var response = await _client.GetAsync("/metrics", admin.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // One of the application's own meters, under Prometheus' naming: dots to underscores, "_total" for a counter.
        Assert.Contains("songly_auth_login_attempts_total{", body, StringComparison.Ordinal);
        Assert.Contains("outcome=\"failure\"", body, StringComparison.Ordinal);

        // What ASP.NET Core records on its own: latency per route.
        Assert.Contains("http_server_request_duration_seconds_bucket{", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The metrics say how many logins fail, how many refresh tokens are reused and what the limiters refuse —
    /// a map for whoever is attacking, so not for anyone else.
    /// </summary>
    [Fact]
    public async Task Nobody_else_reads_them()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "metrics-user");

        await (await _client.GetAsync("/metrics", null)).ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await (await _client.GetAsync("/metrics", user.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
