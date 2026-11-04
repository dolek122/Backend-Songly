using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Songly.NowPlaying.Providers;

/// <summary>
/// A thin HTTP layer over Spotify — no business logic. Every error response, the token endpoint's
/// and 5xx included, leaves here as a <see cref="SpotifyApiException"/> carrying Spotify's error code
/// and <c>Retry-After</c>: without those two, the layer above cannot tell "the user took consent back"
/// from "down for a moment".
/// </summary>
/// <remarks>
/// A named client of <see cref="IHttpClientFactory"/> (<see cref="Name"/>): the factory owns the
/// handlers' lifetime, and the handler is the seam tests replace with a fake Spotify.
/// </remarks>
internal sealed class SpotifyClient(IHttpClientFactory httpClients, IOptions<SpotifyOptions> options)
{
    public const string Name = "Spotify";

    private SpotifyOptions Spotify => options.Value;

    public Task<TokenResponse> ExchangeCodeAsync(string authorizationCode, CancellationToken cancellationToken) =>
        PostTokenAsync(
            new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = authorizationCode, ["redirect_uri"] = Spotify.RedirectUri },
            cancellationToken);

    public Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        PostTokenAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, cancellationToken);

    /// <summary><c>null</c> when nothing is playing — Spotify answers 204 No Content.</summary>
    public async Task<CurrentlyPlayingResponse?> CurrentlyPlayingAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Spotify.ApiBaseUrl}/me/player/currently-playing");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SpotifyApiException(response.StatusCode, error: null, RetryAfter(response));
        }

        return response.StatusCode == HttpStatusCode.NoContent
            ? null
            : await response.Content.ReadFromJsonAsync<CurrentlyPlayingResponse>(cancellationToken);
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Spotify.TokenUrl) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Spotify.ClientId}:{Spotify.ClientSecret}")));
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SpotifyApiException(response.StatusCode, await TokenErrorCodeAsync(response, cancellationToken), RetryAfter(response));
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new SpotifyApiException(response.StatusCode, error: null, retryAfter: null);
    }

    private HttpClient Client() => httpClients.CreateClient(Name);

    /// <summary>
    /// The token endpoint answers with a flat <c>{"error": "..."}</c> (OAuth 2), the Web API with a
    /// nested object — so the code is read only where it has a defined meaning. An unreadable body must
    /// not hide the HTTP error itself, so it reads as no code.
    /// </summary>
    private static async Task<string?> TokenErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<TokenErrorResponse>(body)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Spotify gives <c>Retry-After</c> in seconds. HTTP allows a date too; it is not parsed — no value has a defined behaviour (the poll's own backoff).</summary>
    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Retry-After", out var values)
        && long.TryParse(values.FirstOrDefault()?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    internal sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] long ExpiresInSeconds);

    private sealed record TokenErrorResponse([property: JsonPropertyName("error")] string? Error);

    internal sealed record CurrentlyPlayingResponse(
        [property: JsonPropertyName("is_playing")] bool IsPlaying,
        [property: JsonPropertyName("item")] Item? Item);

    internal sealed record Item(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("artists")] IReadOnlyList<Artist>? Artists,
        [property: JsonPropertyName("album")] Album? Album);

    internal sealed record Artist([property: JsonPropertyName("name")] string? Name);

    internal sealed record Album(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("images")] IReadOnlyList<Image>? Images);

    internal sealed record Image([property: JsonPropertyName("url")] string? Url);
}
