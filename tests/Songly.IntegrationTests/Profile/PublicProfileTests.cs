using System.Net;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// Another user's profile (<c>GET /api/v1/profiles/{userId}</c>), with their favourite genres.
/// Ported from the original <c>PublicProfileIntegrationTest</c>.
/// </summary>
public sealed class PublicProfileTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Returns_the_profile_with_favourite_genres()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "public-viewer");
        var target = await factory.CreateLoggedInUserAsync(_client, "public-target");
        var genre = (await (await _client.GetAsync("/api/v1/genres", target.Token)).ReadJsonAsync()).GetProperty("content")[0];
        await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { genre.GetProperty("id").GetGuid() } }, target.Token);

        var profile = await (await _client.GetAsync($"/api/v1/profiles/{target.Id}", viewer.Token)).ReadJsonAsync();

        Assert.Equal(target.Id, profile.GetProperty("userId").GetGuid());
        Assert.Equal(target.User.Email.Split('@')[0], profile.GetProperty("displayName").GetString());
        var favourite = Assert.Single(profile.GetProperty("favoriteGenres").EnumerateArray());
        Assert.Equal(genre.GetProperty("name").GetString(), favourite.GetProperty("name").GetString());
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "public-notfound");

        await (await _client.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}", viewer.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "PROFILE_NOT_FOUND");
    }

    [Fact]
    public async Task A_malformed_id_is_a_bad_request()
    {
        var viewer = await factory.CreateLoggedInUserAsync(_client, "public-malformed");

        await (await _client.GetAsync("/api/v1/profiles/not-a-uuid", viewer.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_PARAMETER");
    }

    [Fact]
    public async Task Requires_authentication() =>
        await (await _client.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}", accessToken: null)).ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
}
