using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Songly.IntegrationTests.Profile;

/// <summary>The caller's own profile and favourite genres. Ported from the original <c>ProfileFlowIntegrationTest</c>.</summary>
public sealed class ProfileFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_registered_account_has_a_profile_that_can_be_read_and_updated()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "profile-flow");

        var mine = await Json(await _client.GetAsync("/api/v1/profiles/me", user.Token));
        Assert.Equal(user.Id, mine.GetProperty("userId").GetGuid());
        Assert.Equal(user.User.Email.Split('@')[0], mine.GetProperty("displayName").GetString());

        // An old client still sending avatarUrl is not rejected: the field is simply ignored.
        var updated = await Json(await _client.PutJsonAsync("/api/v1/profiles/me", new { displayName = "Ada", bio = "music lover", avatarUrl = "https://tracker.example/pixel.gif" }, user.Token));
        Assert.Equal("Ada", updated.GetProperty("displayName").GetString());
        Assert.Equal("music lover", updated.GetProperty("bio").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("avatarUrl").ValueKind);
    }

    /// <summary>The update replaces both fields: a bio left out of the request is a bio removed.</summary>
    [Fact]
    public async Task Leaving_the_bio_out_removes_it()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "profile-bio");
        await _client.PutJsonAsync("/api/v1/profiles/me", new { displayName = "Ada", bio = "music lover" }, user.Token);

        var updated = await Json(await _client.PutJsonAsync("/api/v1/profiles/me", new { displayName = "Ada" }, user.Token));

        Assert.Equal(JsonValueKind.Null, updated.GetProperty("bio").ValueKind);
    }

    public static TheoryData<string, string?> InvalidProfiles => new()
    {
        { "   ", null },
        { new string('x', 81), null },
        { "Ada", new string('x', 501) },
    };

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public async Task An_invalid_profile_is_rejected(string displayName, string? bio)
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "profile-invalid");

        await (await _client.PutJsonAsync("/api/v1/profiles/me", new { displayName, bio }, user.Token))
            .ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Favourite_genres_can_be_set_and_read_when_known_and_are_rejected_when_unknown()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "genre-flow");
        var genres = (await Json(await _client.GetAsync("/api/v1/genres", user.Token))).GetProperty("content");
        var first = genres[0].GetProperty("id").GetGuid();
        var second = genres[1].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { first, second, first } }, user.Token)).StatusCode);

        var favourites = await Json(await _client.GetAsync("/api/v1/profiles/me/genres", user.Token));
        Assert.Equal([first, second], favourites.EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).Order());

        await (await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { first, Guid.NewGuid() } }, user.Token))
            .ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_GENRE");

        // The rejected request changed nothing.
        Assert.Equal(2, (await Json(await _client.GetAsync("/api/v1/profiles/me/genres", user.Token))).GetArrayLength());
    }

    [Fact]
    public async Task Setting_the_genres_replaces_the_previous_set()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "genre-replace");
        var genres = (await Json(await _client.GetAsync("/api/v1/genres", user.Token))).GetProperty("content");
        await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { genres[0].GetProperty("id").GetGuid(), genres[1].GetProperty("id").GetGuid() } }, user.Token);

        await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { genres[2].GetProperty("id").GetGuid() } }, user.Token);

        var favourite = Assert.Single((await Json(await _client.GetAsync("/api/v1/profiles/me/genres", user.Token))).EnumerateArray());
        Assert.Equal(genres[2].GetProperty("id").GetGuid(), favourite.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task An_empty_set_of_genres_is_rejected()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "genre-empty");

        await (await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = Array.Empty<Guid>() }, user.Token))
            .ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { }, user.Token))
            .ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData("/api/v1/profiles/me")]
    [InlineData("/api/v1/profiles/me/genres")]
    [InlineData("/api/v1/genres")]
    public async Task Profile_endpoints_require_authentication(string path) =>
        await (await _client.GetAsync(path, accessToken: null)).ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
