using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Community;

/// <summary>
/// Appeals against bans (original ADR 0017) and the second instance (ADR 0028). Ported from the
/// original <c>BanAppealIntegrationTest</c> and <c>BanAppealEscalationIntegrationTest</c>; the
/// notification and mail parts arrive with the notification module.
/// </summary>
/// <remarks>
/// The second instance's queue is global and the database shared, so tests look for their own case
/// in it, never at its size.
/// </remarks>
public sealed class BanAppealTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CommunityTestClient _communities = new(factory, factory.CreateClient());

    /// <summary>The appeal's text stays in the pull channel: the moderators read it on the ban list.</summary>
    [Fact]
    public async Task An_appeal_reaches_the_moderators_ban_list()
    {
        var (owner, troll, communityId) = await Banned("appeal-main");

        Assert.Equal(HttpStatusCode.NoContent, (await _communities.AppealAsync(troll, communityId, "przepraszam, to byl zart")).StatusCode);

        var ban = Assert.Single((await _communities.BansAsync(owner, communityId)).EnumerateArray());
        Assert.Equal("przepraszam, to byl zart", ban.GetProperty("appealText").GetString());
        Assert.Equal("PENDING", ban.GetProperty("appealStatus").GetString());
        Assert.Equal("PENDING", (await _communities.StatusAsync(troll, communityId)).GetProperty("ban").GetProperty("appealStatus").GetString());
    }

    /// <summary>
    /// One appeal per ban, whatever became of it: the structural anti-spam of a channel from the
    /// punished to the punisher. A rejection closes the case — appealing again after it would turn
    /// the channel into a way of writing to the moderators as often as one likes.
    /// </summary>
    [Fact]
    public async Task A_second_appeal_is_a_conflict_even_after_a_rejection()
    {
        var (owner, troll, communityId) = await Banned("appeal-second");
        await _communities.AppealAsync(troll, communityId);

        await (await _communities.AppealAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "BAN_APPEAL_ALREADY_SUBMITTED");

        await _communities.RejectAppealAsync(owner, communityId, troll.Id);

        await (await _communities.AppealAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "BAN_APPEAL_ALREADY_SUBMITTED");
    }

    [Fact]
    public async Task There_is_nothing_to_appeal_without_an_active_ban()
    {
        var (owner, troll, communityId) = await Banned("appeal-none");
        var outsider = await factory.CreateLoggedInUserAsync(_client, "appeal-none-outsider");
        await (await _communities.AppealAsync(outsider, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "NO_BAN_TO_APPEAL");

        await _communities.ExpireBanAsync(communityId, troll.Id);

        await (await _communities.AppealAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "NO_BAN_TO_APPEAL");
        _ = owner;
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_appeal_needs_a_text(string? text)
    {
        var (_, troll, communityId) = await Banned("appeal-blank");

        await (await _client.PostJsonAsync($"/api/v1/communities/{communityId}/ban-appeal", new { text }, troll.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Accepting an appeal is lifting the ban — no separate endpoint, because it is the same decision.</summary>
    [Fact]
    public async Task Lifting_a_ban_with_a_pending_appeal_lets_the_user_return()
    {
        var (owner, troll, communityId) = await Banned("appeal-accepted");
        await _communities.AppealAsync(troll, communityId);

        Assert.Equal(HttpStatusCode.NoContent, (await _communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);

        await _communities.JoinedAsync(troll, communityId);
    }

    /// <summary>Rejection: the ban stays, and the appellant sees the answer.</summary>
    [Fact]
    public async Task A_rejected_appeal_keeps_the_ban()
    {
        var (owner, troll, communityId) = await Banned("appeal-rejected");
        await _communities.AppealAsync(troll, communityId);

        Assert.Equal(HttpStatusCode.NoContent, (await _communities.RejectAppealAsync(owner, communityId, troll.Id)).StatusCode);

        Assert.Equal("REJECTED", (await _communities.StatusAsync(troll, communityId)).GetProperty("ban").GetProperty("appealStatus").GetString());
        await (await _communities.JoinAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");
        await (await _communities.RejectAppealAsync(owner, communityId, troll.Id)).ReadErrorAsync(HttpStatusCode.NotFound, "BAN_APPEAL_NOT_FOUND");
    }

    [Fact]
    public async Task Rejecting_an_appeal_takes_a_moderator()
    {
        var (owner, troll, communityId) = await Banned("appeal-reject-who");
        var member = await factory.CreateLoggedInUserAsync(_client, "appeal-reject-who-member");
        await _communities.JoinedAsync(member, communityId);
        await _communities.AppealAsync(troll, communityId);

        await (await _communities.RejectAppealAsync(member, communityId, troll.Id)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MODERATOR");
        _ = owner;
    }

    /// <summary>A new ban after an expired one clears the appeal: a new case, a fresh right — even after a rejection.</summary>
    [Fact]
    public async Task A_reimposed_ban_grants_a_fresh_appeal()
    {
        var (owner, troll, communityId) = await Banned("appeal-fresh", durationDays: 1);
        await _communities.AppealAsync(troll, communityId);
        await _communities.RejectAppealAsync(owner, communityId, troll.Id);
        await _communities.ExpireBanAsync(communityId, troll.Id);

        await _communities.BannedAsync(owner, communityId, troll.Id, "znowu");

        Assert.Equal(JsonValueKind.Null, (await _communities.StatusAsync(troll, communityId)).GetProperty("ban").GetProperty("appealStatus").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await _communities.AppealAsync(troll, communityId)).StatusCode);
    }

    /// <summary>The second instance is entered only after the first one refused — the community's moderators are the default instance.</summary>
    [Fact]
    public async Task Escalation_requires_a_rejected_appeal_first()
    {
        var (_, troll, communityId) = await Banned("escalation-early");
        await (await _communities.EscalateAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "BAN_APPEAL_NOT_ESCALATABLE");

        await _communities.AppealAsync(troll, communityId);

        await (await _communities.EscalateAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "BAN_APPEAL_NOT_ESCALATABLE");
    }

    /// <summary>The administrator decides between two sides, so the queue shows both: the ban's reason and the appeal.</summary>
    [Fact]
    public async Task An_escalation_reaches_the_admins_with_both_sides_of_the_case()
    {
        var admin = await factory.CreateAdminAsync(_client, "escalation-admin");
        var (owner, troll, communityId) = await Escalated("escalation-main", reason: "nekanie innych");

        var mine = (await Queue(admin)).Single(c => c.GetProperty("communityId").GetGuid() == communityId);

        Assert.Equal(troll.Id, mine.GetProperty("userId").GetGuid());
        Assert.Equal("nekanie innych", mine.GetProperty("reason").GetString());
        Assert.Equal("to nieporozumienie", mine.GetProperty("appealText").GetString());
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("escalatedAt").ValueKind);
        Assert.Equal("ESCALATED", (await _communities.StatusAsync(troll, communityId)).GetProperty("ban").GetProperty("appealStatus").GetString());
        _ = owner;
    }

    /// <summary>Overturned by the second instance: the user can return, and the case leaves the queue.</summary>
    [Fact]
    public async Task The_admin_can_overturn_the_ban()
    {
        var admin = await factory.CreateAdminAsync(_client, "overturn-admin");
        var (_, troll, communityId) = await Escalated("overturn");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}", admin.Token)).StatusCode);

        await _communities.JoinedAsync(troll, communityId);
        Assert.DoesNotContain(await Queue(admin), c => c.GetProperty("communityId").GetGuid() == communityId);
    }

    /// <summary>Kept by the second instance — the end of the ladder: there is nobody above the administration.</summary>
    [Fact]
    public async Task The_admin_can_reject_the_escalation_and_that_is_final()
    {
        var admin = await factory.CreateAdminAsync(_client, "final-admin");
        var (_, troll, communityId) = await Escalated("final");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}/escalation-rejection", new { }, admin.Token)).StatusCode);

        Assert.Equal("ESCALATION_REJECTED", (await _communities.StatusAsync(troll, communityId)).GetProperty("ban").GetProperty("appealStatus").GetString());
        await (await _communities.EscalateAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "BAN_APPEAL_NOT_ESCALATABLE");
        await (await _client.PostJsonAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}/escalation-rejection", new { }, admin.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "ESCALATED_APPEAL_NOT_FOUND");
    }

    /// <summary>The administration does not review bans on its own initiative: without an escalation there is nothing to overturn.</summary>
    [Fact]
    public async Task The_admin_cannot_touch_a_ban_without_an_escalation()
    {
        var admin = await factory.CreateAdminAsync(_client, "untouched-admin");
        var (_, troll, communityId) = await Banned("untouched");

        await (await _client.DeleteAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}", admin.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "ESCALATED_APPEAL_NOT_FOUND");
        await (await _communities.JoinAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");
    }

    [Fact]
    public async Task The_second_instance_is_for_admins_only()
    {
        var (owner, troll, communityId) = await Escalated("admins-only");

        await (await _client.GetAsync("/api/v1/admin/ban-appeals", owner.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await _client.DeleteAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}", owner.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    private async Task<(LoggedInUser Owner, LoggedInUser Troll, Guid CommunityId)> Banned(string prefix, string reason = "spam", int? durationDays = null)
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-owner");
        var troll = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-troll");
        var communityId = await _communities.CreateAsync(owner, prefix);
        await _communities.JoinedAsync(troll, communityId);
        await _communities.BannedAsync(owner, communityId, troll.Id, reason, durationDays);
        return (owner, troll, communityId);
    }

    private async Task<(LoggedInUser Owner, LoggedInUser Troll, Guid CommunityId)> Escalated(string prefix, string reason = "spam")
    {
        var (owner, troll, communityId) = await Banned(prefix, reason);
        await _communities.AppealAsync(troll, communityId);
        await _communities.RejectAppealAsync(owner, communityId, troll.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await _communities.EscalateAsync(troll, communityId)).StatusCode);
        return (owner, troll, communityId);
    }

    private async Task<List<JsonElement>> Queue(LoggedInUser admin) =>
        (await (await _client.GetAsync("/api/v1/admin/ban-appeals", admin.Token)).ReadJsonAsync()).EnumerateArray().ToList();
}
