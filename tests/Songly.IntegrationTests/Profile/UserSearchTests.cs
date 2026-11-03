using System.Net;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// Finding people to add as friends (<c>GET /api/v1/profiles/search</c>). Ported from the original
/// <c>UserSearchIntegrationTest</c>.
/// </summary>
public sealed class UserSearchTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Finds_matching_users_case_insensitively_and_leaves_out_the_searcher()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var searcher = await factory.CreateLoggedInUserAsync(_client, "search-searcher");
        var melomanka = await factory.CreateLoggedInUserAsync(_client, "search-melomanka");
        var meloman = await factory.CreateLoggedInUserAsync(_client, "search-meloman");
        await _client.RenameAsync(searcher, $"{tag} Meloman sam");
        await _client.RenameAsync(melomanka, $"{tag} Melomanka");
        await _client.RenameAsync(meloman, $"{tag} meloman");

        // "MELOMAN" matches all three, case-insensitively — but not the one searching.
        var found = await _client.SearchAsync(searcher, $"{tag} MELOMAN".ToUpperInvariant());

        Assert.Equal(2, found.Count);
        Assert.Contains(melomanka.Id, found);
        Assert.Contains(meloman.Id, found);
    }

    [Fact]
    public async Task The_searcher_never_finds_themselves()
    {
        var solo = await factory.CreateLoggedInUserAsync(_client, "search-solo");

        Assert.Empty(await _client.SearchAsync(solo, solo.User.Email.Split('@')[0]));
    }

    [Fact]
    public async Task A_blank_query_returns_an_empty_page_not_everyone()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "search-blank");

        var page = await (await _client.GetAsync("/api/v1/profiles/search?query=%20%20%20", user.Token)).ReadJsonAsync();

        Assert.Equal(0, page.GetProperty("content").GetArrayLength());
        Assert.Equal(0, page.GetProperty("page").GetProperty("totalElements").GetInt64());
    }

    /// <summary>
    /// <c>%</c> and <c>_</c> are characters, not LIKE patterns. Without escaping, "%" returned every
    /// user in turn — getting round "a blank query returns nobody" with one character.
    /// </summary>
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("%_")]
    [InlineData("\\")]
    public async Task Like_wildcards_are_literals(string query)
    {
        var searcher = await factory.CreateLoggedInUserAsync(_client, "search-wildcard");

        var page = await (await _client.GetAsync($"/api/v1/profiles/search?query={Uri.EscapeDataString(query)}&size=100", searcher.Token)).ReadJsonAsync();

        // Test accounts are named after their address (letters, digits, hyphens), so a match on
        // them means the character was treated as a pattern.
        Assert.All(page.GetProperty("content").EnumerateArray(), hit => Assert.Contains(query, hit.GetProperty("displayName").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_with_wildcards_is_found_by_them()
    {
        var searcher = await factory.CreateLoggedInUserAsync(_client, "search-wildcard-searcher");
        var fan = await factory.CreateLoggedInUserAsync(_client, "search-wildcard-fan");
        await _client.RenameAsync(fan, $"Fan na 100%_procent {Guid.NewGuid():N}");

        Assert.Contains(fan.Id, await _client.SearchAsync(searcher, "100%_proc"));
    }

    [Fact]
    public async Task Without_a_query_it_is_a_bad_request()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "search-noparam");

        var error = await (await _client.GetAsync("/api/v1/profiles/search", user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "MISSING_PARAMETER");
        Assert.Equal("Brak wymaganego parametru: query", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Requires_authentication() =>
        await (await _client.GetAsync("/api/v1/profiles/search?query=meloman", accessToken: null)).ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
}
