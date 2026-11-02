using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Managing the administrator role (original ADR 0021). Ported from the original
/// <c>AdminRoleIntegrationTest</c>: the manual <c>UPDATE</c> is no longer needed, and the path to
/// privilege escalation has closed edges.
/// </summary>
public sealed class AdminRoleTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<List<Guid>> Admins(LoggedInUser admin)
    {
        var response = await _client.GetAsync("/api/v1/admin/admins", admin.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return json.GetProperty("userIds").EnumerateArray().Select(id => id.GetGuid()).ToList();
    }

    /// <summary>
    /// Promotion works and gives real rights at once — checked on an endpoint gated by the role,
    /// because only that proves the column means anything. No new login needed: the role is read live.
    /// </summary>
    [Fact]
    public async Task An_admin_promotes_another_user_who_immediately_gains_access()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var promoted = await factory.CreateLoggedInUserAsync(_client, "promoted");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/v1/admin/admins", promoted.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{promoted.Id}/admin", new { }, admin.Token)).StatusCode);

        Assert.Contains(promoted.Id, await Admins(promoted));
    }

    /// <summary>Demotion works live too — no session to invalidate, because the role is not in the token.</summary>
    [Fact]
    public async Task A_revoked_admin_loses_access_immediately()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var demoted = await factory.CreateAdminAsync(_client, "demoted");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{demoted.Id}/admin", admin.Token)).StatusCode);

        await (await _client.GetAsync("/api/v1/admin/admins", demoted.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.DoesNotContain(demoted.Id, await Admins(admin));
    }

    /// <summary>
    /// The invariant: nobody demotes themselves, and only an admin demotes — so at least one
    /// administrator always remains.
    /// </summary>
    [Fact]
    public async Task An_admin_cannot_revoke_their_own_role()
    {
        var admin = await factory.CreateAdminAsync(_client);

        await (await _client.DeleteAsync($"/api/v1/admin/users/{admin.Id}/admin", admin.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "CANNOT_REVOKE_OWN_ADMIN");

        Assert.Contains(admin.Id, await Admins(admin));
    }

    /// <summary>A regular user touches nothing under /admin — the rule sits on the prefix.</summary>
    [Fact]
    public async Task A_regular_user_is_rejected_on_every_admin_endpoint()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "regular");
        var target = Guid.NewGuid();

        var responses = new[]
        {
            await _client.GetAsync("/api/v1/admin/admins", user.Token),
            await _client.PutJsonAsync($"/api/v1/admin/users/{target}/admin", new { }, user.Token),
            await _client.DeleteAsync($"/api/v1/admin/users/{target}/admin", user.Token),
            await _client.GetAsync("/api/v1/admin/suspensions", user.Token),
            await _client.PutJsonAsync($"/api/v1/admin/users/{target}/suspension", new { reason = "x" }, user.Token),
            await _client.DeleteAsync($"/api/v1/admin/users/{target}/suspension", user.Token),
            await _client.PostJsonAsync($"/api/v1/admin/users/{target}/suspension/appeal/rejection", new { }, user.Token),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }

    [Fact]
    public async Task Granting_to_an_unknown_user_is_404()
    {
        var admin = await factory.CreateAdminAsync(_client);

        await (await _client.PutJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}/admin", new { }, admin.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "USER_NOT_FOUND");
    }

    [Fact]
    public async Task Granting_and_revoking_are_idempotent()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var other = await factory.CreateAdminAsync(_client, "already-admin");
        var regular = await factory.CreateUserAsync("never-admin");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{other.Id}/admin", new { }, admin.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{regular.Id}/admin", admin.Token)).StatusCode);
    }

    /// <summary>The list carries ids only — no e-mail addresses — and is not paged.</summary>
    [Fact]
    public async Task The_admin_list_has_ids_only()
    {
        var admin = await factory.CreateAdminAsync(_client);

        var response = await _client.GetAsync("/api/v1/admin/admins", admin.Token);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(admin.Id.ToString(), body, StringComparison.Ordinal);
        Assert.DoesNotContain("@", body, StringComparison.Ordinal);
    }
}
