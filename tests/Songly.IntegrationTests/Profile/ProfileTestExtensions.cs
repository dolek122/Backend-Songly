using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Songly.IntegrationTests.Profile;

internal static class ProfileTestExtensions
{
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    public static async Task RenameAsync(this HttpClient client, LoggedInUser user, string displayName) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PutJsonAsync("/api/v1/profiles/me", new { displayName }, user.Token)).StatusCode);

    /// <summary>The ids on the first page of a user search.</summary>
    public static async Task<IReadOnlyList<Guid>> SearchAsync(this HttpClient client, LoggedInUser viewer, string query) =>
        (await (await client.GetAsync($"/api/v1/profiles/search?query={Uri.EscapeDataString(query)}", viewer.Token)).ReadJsonAsync())
            .GetProperty("content").EnumerateArray().Select(hit => hit.GetProperty("userId").GetGuid()).ToList();
}
