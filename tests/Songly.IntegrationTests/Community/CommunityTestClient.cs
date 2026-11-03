using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Community;

/// <summary>The community calls the ban tests make over and over.</summary>
internal sealed class CommunityTestClient(SonglyApiFactory factory, HttpClient client)
{
    public async Task<Guid> CreateAsync(LoggedInUser owner, string prefix)
    {
        var response = await client.PostJsonAsync("/api/v1/communities", new { name = $"{prefix} {Guid.NewGuid():N}" }, owner.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    public Task<HttpResponseMessage> JoinAsync(LoggedInUser user, Guid communityId) =>
        client.PutJsonAsync($"/api/v1/communities/{communityId}/membership", new { }, user.Token);

    public async Task JoinedAsync(LoggedInUser user, Guid communityId) =>
        Assert.Equal(HttpStatusCode.NoContent, (await JoinAsync(user, communityId)).StatusCode);

    public Task<HttpResponseMessage> SetRoleAsync(LoggedInUser requester, Guid communityId, Guid userId, string role) =>
        client.PutJsonAsync($"/api/v1/communities/{communityId}/members/{userId}/role", new { role }, requester.Token);

    public Task<HttpResponseMessage> BanAsync(LoggedInUser requester, Guid communityId, Guid userId, string? reason = null, int? durationDays = null) =>
        client.PutJsonAsync($"/api/v1/communities/{communityId}/bans/{userId}", new { reason, durationDays }, requester.Token);

    public async Task BannedAsync(LoggedInUser requester, Guid communityId, Guid userId, string? reason = "spam", int? durationDays = null) =>
        Assert.Equal(HttpStatusCode.NoContent, (await BanAsync(requester, communityId, userId, reason, durationDays)).StatusCode);

    public Task<HttpResponseMessage> UnbanAsync(LoggedInUser requester, Guid communityId, Guid userId) =>
        client.DeleteAsync($"/api/v1/communities/{communityId}/bans/{userId}", requester.Token);

    public Task<HttpResponseMessage> AppealAsync(LoggedInUser user, Guid communityId, string text = "to nieporozumienie") =>
        client.PostJsonAsync($"/api/v1/communities/{communityId}/ban-appeal", new { text }, user.Token);

    public Task<HttpResponseMessage> RejectAppealAsync(LoggedInUser requester, Guid communityId, Guid userId) =>
        client.PostJsonAsync($"/api/v1/communities/{communityId}/bans/{userId}/appeal/rejection", new { }, requester.Token);

    public Task<HttpResponseMessage> EscalateAsync(LoggedInUser user, Guid communityId) =>
        client.PostJsonAsync($"/api/v1/communities/{communityId}/ban-appeal/escalation", new { }, user.Token);

    public async Task<JsonElement> StatusAsync(LoggedInUser user, Guid communityId) =>
        await (await client.GetAsync($"/api/v1/communities/{communityId}/membership", user.Token)).ReadJsonAsync();

    public async Task<JsonElement> BansAsync(LoggedInUser moderator, Guid communityId) =>
        (await (await client.GetAsync($"/api/v1/communities/{communityId}/bans", moderator.Token)).ReadJsonAsync()).GetProperty("content");

    /// <summary>Moves a ban's expiry into the past — its age is what the test is about.</summary>
    public Task ExpireBanAsync(Guid communityId, Guid userId) =>
        factory.ExecuteSqlAsync(
            "UPDATE community_bans SET expires_at = now() - interval '1 hour' WHERE community_id = @community AND user_id = @user",
            ("community", communityId),
            ("user", userId));

    public Task<HttpResponseMessage> DeleteAccountAsync(LoggedInUser user)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/me") { Content = JsonContent.Create(new { password = user.User.Password }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
