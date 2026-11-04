using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Notification;

/// <summary>The inbox, read the way the mobile app reads it.</summary>
internal sealed class NotificationTestClient(HttpClient client)
{
    public async Task<JsonElement> PageAsync(LoggedInUser user)
    {
        using var response = await client.GetAsync("/api/v1/notifications?size=100", user.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    public async Task<IReadOnlyList<JsonElement>> ListAsync(LoggedInUser user) =>
        [.. (await PageAsync(user)).GetProperty("content").EnumerateArray()];

    public async Task<IReadOnlyList<JsonElement>> OfTypeAsync(LoggedInUser user, string type) =>
        [.. (await ListAsync(user)).Where(n => n.GetProperty("type").GetString() == type)];

    public async Task<long> UnreadCountAsync(LoggedInUser user)
    {
        using var response = await client.GetAsync("/api/v1/notifications/unread-count", user.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("count").GetInt64();
    }

    public Task<HttpResponseMessage> MarkReadAsync(LoggedInUser user, Guid notificationId) =>
        client.PostJsonAsync($"/api/v1/notifications/{notificationId}/read", new { }, user.Token);

    public async Task ReadAllAsync(LoggedInUser user) =>
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/v1/notifications/read-all", new { }, user.Token)).StatusCode);
}
