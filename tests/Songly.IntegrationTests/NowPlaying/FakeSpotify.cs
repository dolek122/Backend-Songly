using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Songly.IntegrationTests.NowPlaying;

/// <summary>
/// Spotify replaced at the seam .NET gives for it: the primary <see cref="HttpMessageHandler"/> of the
/// app's named client. Everything above it — headers, the form, the JSON, the status handling — runs
/// as in production. Each endpoint answers with what the test set, and every request is recorded.
/// </summary>
internal sealed class FakeSpotify
{
    /// <summary>
    /// <c>expires_in=0</c> by default: the next call goes the token-refresh way at once — the way most
    /// of these tests are about.
    /// </summary>
    public static readonly Func<HttpResponseMessage> ExpiringToken = () => Json(HttpStatusCode.OK, """{"access_token":"access","refresh_token":"refresh","expires_in":0}""");

    public Func<HttpResponseMessage> Token { get; set; } = ExpiringToken;

    public Func<HttpResponseMessage> Api { get; set; } = () => new HttpResponseMessage(HttpStatusCode.NoContent);

    public List<(HttpMethod Method, string Path, string? Authorization, string Body)> Requests { get; } = [];

    public int ApiCalls => Requests.Count(r => r.Path.StartsWith("/v1", StringComparison.Ordinal));

    /// <summary>A second instance of the app on the shared database and Redis, talking to this fake.</summary>
    public WebApplicationFactory<Program> Attach(SonglyApiFactory factory) =>
        factory.WithSettings(
            ("Songly:NowPlaying:Spotify:TokenUrl", "https://spotify.test/api/token"),
            ("Songly:NowPlaying:Spotify:ApiBaseUrl", "https://spotify.test/v1"),
            ("Songly:NowPlaying:Spotify:ClientId", "client-id"),
            ("Songly:NowPlaying:Spotify:ClientSecret", "client-secret"))
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient("Spotify").ConfigurePrimaryHttpMessageHandler(() => new Handler(this))));

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Playing(string title, string artists, string album, string image) =>
        Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            is_playing = true,
            item = new
            {
                name = title,
                artists = artists.Split(", ").Select(name => new { name }),
                album = new { name = album, images = new[] { new { url = image }, new { url = "https://i.scdn.co/small" } } },
            },
        }));

    private sealed class Handler(FakeSpotify spotify) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            lock (spotify.Requests)
            {
                spotify.Requests.Add((request.Method, path, request.Headers.Authorization?.ToString(), body));
            }

            return path == "/api/token" ? spotify.Token() : spotify.Api();
        }
    }
}
