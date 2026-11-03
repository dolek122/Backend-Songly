using System.Net;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// A suspended account's posts are hidden (original ADR 0026) — and come back in full when the
/// suspension is lifted or expires. The posts part of the original
/// <c>SuspendedContentHiddenIntegrationTest</c>; the profile part is in <c>SuspendedProfileTests</c>.
/// </summary>
public sealed class SuspendedContentTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());

    [Fact]
    public async Task A_suspension_hides_the_posts_from_lists_and_direct_reads()
    {
        var (admin, viewer, troll) = await Scenario("hidden-main");
        var postId = await _content.PublishAsync(troll, "treść do ukrycia");
        Assert.True(await PublicListContains(viewer, postId));

        await Suspend(admin, troll, durationDays: null);

        Assert.False(await PublicListContains(viewer, postId));
        await (await _content.GetPostAsync(viewer, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    /// <summary>
    /// A hidden post is out of reach for the write paths too — otherwise one could comment on and like
    /// content nobody can see, and its author would get notified about activity under an invisible post.
    /// </summary>
    [Fact]
    public async Task A_hidden_post_cannot_be_liked_or_commented()
    {
        var (admin, viewer, troll) = await Scenario("hidden-write");
        var postId = await _content.PublishAsync(troll, "treść");

        await Suspend(admin, troll, durationDays: null);

        await (await _content.LikeAsync(viewer, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
        await (await _content.CommentAsync(viewer, postId)).ReadErrorAsync(HttpStatusCode.NotFound, "POST_NOT_FOUND");
    }

    /// <summary>The sanction does not spill over onto others.</summary>
    [Fact]
    public async Task Other_accounts_are_unaffected()
    {
        var (admin, viewer, troll) = await Scenario("hidden-other");
        var innocent = await factory.CreateLoggedInUserAsync(_client, "hidden-other-innocent");
        var trollPost = await _content.PublishAsync(troll, "treść trolla");
        var innocentPost = await _content.PublishAsync(innocent, "treść niewinnego");

        await Suspend(admin, troll, durationDays: null);

        Assert.False(await PublicListContains(viewer, trollPost));
        Assert.True(await PublicListContains(viewer, innocentPost));
    }

    /// <summary>
    /// The most important test of this rule: <b>hidden when read, never deleted</b>. Suspensions are
    /// temporary and lifted, so the posts must come back whole — otherwise a suspension would be a
    /// heavier penalty than itself.
    /// </summary>
    [Fact]
    public async Task Lifting_and_expiry_restore_everything()
    {
        var (admin, viewer, troll) = await Scenario("hidden-restore");
        var postId = await _content.PublishAsync(troll, "treść wracająca");
        var commentedPost = await _content.PublishAsync(viewer, "post z komentarzem trolla");
        await _content.CommentedAsync(troll, commentedPost);

        await Suspend(admin, troll, durationDays: null);
        Assert.False(await PublicListContains(viewer, postId));
        Assert.Equal(0, (await _content.PostAsync(viewer, commentedPost)).GetProperty("commentCount").GetInt64());
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{troll.Id}/suspension", admin.Token)).StatusCode);
        Assert.True(await PublicListContains(viewer, postId));
        Assert.Equal(1, (await _content.PostAsync(viewer, commentedPost)).GetProperty("commentCount").GetInt64());

        // Expiry too — without anyone's action (original ADR 0014).
        await Suspend(admin, troll, durationDays: 7);
        Assert.False(await PublicListContains(viewer, postId));
        await factory.ExecuteSqlAsync("UPDATE users SET suspension_expires_at = now() - interval '1 hour' WHERE id = @id", ("id", troll.Id));

        Assert.True(await PublicListContains(viewer, postId));
    }

    private async Task<(LoggedInUser Admin, LoggedInUser Viewer, LoggedInUser Troll)> Scenario(string prefix) =>
        (await factory.CreateAdminAsync(_client, $"{prefix}-admin"),
            await factory.CreateLoggedInUserAsync(_client, $"{prefix}-viewer"),
            await factory.CreateLoggedInUserAsync(_client, $"{prefix}-troll"));

    private async Task Suspend(LoggedInUser admin, LoggedInUser target, int? durationDays) =>
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await _client.PutJsonAsync($"/api/v1/admin/users/{target.Id}/suspension", new { reason = "nękanie", durationDays }, admin.Token)).StatusCode);

    private async Task<bool> PublicListContains(LoggedInUser viewer, Guid postId) =>
        (await _content.PageAsync(viewer, "/api/v1/posts?size=100")).GetProperty("content").EnumerateArray().Any(p => p.GetProperty("id").GetGuid() == postId);
}
