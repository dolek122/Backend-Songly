using System.Net;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// A suspended account's profile is hidden (original ADR 0026) — from the search and from a direct
/// link alike — and comes back in full when the suspension is lifted or expires. The profile part
/// of the original <c>SuspendedContentHiddenIntegrationTest</c>; the posts part comes with content.
/// </summary>
public sealed class SuspendedProfileTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_suspension_hides_the_profile_and_the_search_results()
    {
        var (admin, viewer, troll, name) = await Scenario("hidden-main");
        Assert.Contains(troll.Id, await _client.SearchAsync(viewer, name));

        await Suspend(admin, troll, durationDays: null);

        Assert.DoesNotContain(troll.Id, await _client.SearchAsync(viewer, name));
        await (await _client.GetAsync($"/api/v1/profiles/{troll.Id}", viewer.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "PROFILE_NOT_FOUND");
    }

    /// <summary>The sanction does not spill over onto others.</summary>
    [Fact]
    public async Task Other_accounts_are_unaffected()
    {
        var (admin, viewer, troll, name) = await Scenario("hidden-other");
        var innocent = await factory.CreateLoggedInUserAsync(_client, "hidden-other-innocent");
        await _client.RenameAsync(innocent, $"Niewinny {name}");

        await Suspend(admin, troll, durationDays: null);

        Assert.Equal([innocent.Id], await _client.SearchAsync(viewer, name));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/v1/profiles/{innocent.Id}", viewer.Token)).StatusCode);
    }

    /// <summary>
    /// The most important test of the change: <b>hidden when read, never deleted</b>. Suspensions
    /// can be temporary and lifted, so everything must come back — otherwise a suspension would be
    /// a heavier penalty than the suspension itself.
    /// </summary>
    [Fact]
    public async Task Lifting_and_expiry_restore_everything()
    {
        var (admin, viewer, troll, name) = await Scenario("hidden-restore");

        // Lifting the suspension restores the profile.
        await Suspend(admin, troll, durationDays: null);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{troll.Id}/suspension", admin.Token)).StatusCode);
        Assert.Contains(troll.Id, await _client.SearchAsync(viewer, name));

        // So does the term running out — with nobody doing anything (original ADR 0014).
        await Suspend(admin, troll, durationDays: 7);
        Assert.DoesNotContain(troll.Id, await _client.SearchAsync(viewer, name));
        await factory.ExecuteSqlAsync("UPDATE users SET suspension_expires_at = now() - interval '1 hour' WHERE id = @id", ("id", troll.Id));

        Assert.Contains(troll.Id, await _client.SearchAsync(viewer, name));
        var profile = await (await _client.GetAsync($"/api/v1/profiles/{troll.Id}", viewer.Token)).ReadJsonAsync();
        Assert.Equal(name, profile.GetProperty("displayName").GetString());
    }

    private async Task<(LoggedInUser Admin, LoggedInUser Viewer, LoggedInUser Troll, string Name)> Scenario(string prefix)
    {
        var admin = await factory.CreateAdminAsync(_client, $"{prefix}-admin");
        var viewer = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-viewer");
        var troll = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-troll");
        var name = $"Nekacz {Guid.NewGuid():N}";
        await _client.RenameAsync(troll, name);
        return (admin, viewer, troll, name);
    }

    private async Task Suspend(LoggedInUser admin, LoggedInUser target, int? durationDays) =>
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _client.PutJsonAsync($"/api/v1/admin/users/{target.Id}/suspension", new { reason = "nękanie", durationDays }, admin.Token)).StatusCode);
}
