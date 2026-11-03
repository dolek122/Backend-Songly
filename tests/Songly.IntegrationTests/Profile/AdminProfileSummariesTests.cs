using System.Net;
using System.Text.Json;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// Names for the administration (original ADR 0036). The point: the administrator sees the name of
/// a suspended user whose profile is hidden from everyone (original ADR 0026) — the admin lists
/// carried bare ids, and fetching the name from the profile ended in a 404 exactly for the people
/// the administrator decides about. Ported from the original <c>AdminProfileSummariesIntegrationTest</c>.
/// </summary>
public sealed class AdminProfileSummariesTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task The_admin_sees_the_name_of_a_suspended_user_whose_profile_is_hidden()
    {
        var admin = await factory.CreateAdminAsync(_client, "summaries-admin");
        var target = await factory.CreateLoggedInUserAsync(_client, "summaries-target");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{target.Id}/suspension", new { reason = "nękanie" }, admin.Token)).StatusCode);

        // The starting point: the ordinary way to the name is closed to the admin too.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/profiles/{target.Id}", admin.Token)).StatusCode);

        var summaries = await Summaries($"userIds={target.Id}&userIds={admin.Id}", admin);

        Assert.Equal(2, summaries.Count);
        var suspended = summaries.Single(s => s.GetProperty("userId").GetGuid() == target.Id);
        Assert.Equal(target.User.Email.Split('@')[0], suspended.GetProperty("displayName").GetString());
        Assert.True(suspended.GetProperty("suspended").GetBoolean());
        Assert.False(summaries.Single(s => s.GetProperty("userId").GetGuid() == admin.Id).GetProperty("suspended").GetBoolean());
    }

    /// <summary>The mobile app sends the ids as one comma-separated value; Spring accepted both forms.</summary>
    [Fact]
    public async Task Ids_can_also_come_comma_separated()
    {
        var admin = await factory.CreateAdminAsync(_client, "summaries-comma");
        var other = await factory.CreateLoggedInUserAsync(_client, "summaries-comma-other");

        var summaries = await Summaries($"userIds={admin.Id},{other.Id}", admin);

        Assert.Equal(new[] { admin.Id, other.Id }.Order(), summaries.Select(s => s.GetProperty("userId").GetGuid()).Order());
    }

    /// <summary>Under /api/v1/admin — the role is guarded by path, not by the endpoint.</summary>
    [Fact]
    public async Task A_regular_user_cannot_use_it()
    {
        var regular = await factory.CreateLoggedInUserAsync(_client, "summaries-regular");

        await (await _client.GetAsync($"/api/v1/admin/profiles?userIds={regular.Id}", regular.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    /// <summary>A deleted account in the moderation history is the normal case — absent, not an error.</summary>
    [Fact]
    public async Task Unknown_ids_are_skipped()
    {
        var admin = await factory.CreateAdminAsync(_client, "summaries-unknown");

        Assert.Empty(await Summaries($"userIds={Guid.NewGuid()}", admin));
    }

    [Fact]
    public async Task Too_many_ids_are_rejected()
    {
        var admin = await factory.CreateAdminAsync(_client, "summaries-limit");
        var ids = string.Join(',', Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));

        var error = await (await _client.GetAsync($"/api/v1/admin/profiles?userIds={ids}", admin.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "TOO_MANY_USER_IDS");
        Assert.Equal("Można zapytać o najwyżej 100 użytkowników naraz", error.GetProperty("message").GetString());
    }

    /// <summary>A set, as in the original: an id listed twice counts once towards the limit.</summary>
    [Fact]
    public async Task Duplicates_count_once()
    {
        var admin = await factory.CreateAdminAsync(_client, "summaries-duplicates");
        var ids = string.Join(',', Enumerable.Repeat(admin.Id, 150));

        Assert.Single(await Summaries($"userIds={ids}", admin));
    }

    [Theory]
    [InlineData("", "MISSING_PARAMETER", "Brak wymaganego parametru: userIds")]
    [InlineData("?userIds=not-a-uuid", "INVALID_PARAMETER", "Nieprawidłowa wartość parametru: userIds")]
    public async Task Missing_or_malformed_ids_are_a_bad_request(string query, string code, string message)
    {
        var admin = await factory.CreateAdminAsync(_client, "summaries-bad");

        var error = await (await _client.GetAsync($"/api/v1/admin/profiles{query}", admin.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, code);
        Assert.Equal(message, error.GetProperty("message").GetString());
    }

    private async Task<List<JsonElement>> Summaries(string query, LoggedInUser admin) =>
        (await (await _client.GetAsync($"/api/v1/admin/profiles?{query}", admin.Token)).ReadJsonAsync()).EnumerateArray().ToList();
}
