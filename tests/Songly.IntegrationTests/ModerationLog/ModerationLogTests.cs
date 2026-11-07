using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.ModerationLog;

/// <summary>
/// The moderation history (original ADR 0024, 0027, 0031). Ported from the original
/// <c>ModerationLogIntegrationTest</c>, with the entries it did not check (upheld and dismissed reports, removed
/// comments, the second instance, moderator roles, suspension appeals) added.
/// </summary>
/// <remarks>
/// It closes a debt of four ADRs at once: expired bans were invisible (0014), nobody counted appeals (0017,
/// 0023), and the accuracy of reports could not be measured, because deleting a post took its reports with it
/// (0019).
/// </remarks>
public sealed class ModerationLogTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ReportTestClient _reports = new(factory, factory.CreateClient());

    private CommunityTestClient Communities => _reports.Communities;

    private ContentTestClient Content => _reports.Content;

    /// <summary>
    /// The point of original ADR 0014: an expired ban disappears from the list of state but stays in the history —
    /// only that tells "never banned" from "the ban expired".
    /// </summary>
    [Fact]
    public async Task An_expired_ban_leaves_the_list_of_bans_but_stays_in_the_history()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modlog-expired");
        await Communities.BannedAsync(owner, communityId, troll.Id, durationDays: 7);
        await Communities.ExpireBanAsync(communityId, troll.Id);

        Assert.Empty((await Communities.BansAsync(owner, communityId)).EnumerateArray());

        var entry = Assert.Single(await CommunityLogAsync(owner, communityId), e => Action(e) == "COMMUNITY_BAN_ISSUED");
        Assert.Equal(communityId, entry.GetProperty("communityId").GetGuid());
        Assert.Equal(troll.Id, entry.GetProperty("subjectId").GetGuid());
        Assert.Equal(owner.Id, entry.GetProperty("actorId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, entry.GetProperty("expiresAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("reportCount").ValueKind);
    }

    /// <summary>A ban and its lifting are two entries, newest first — otherwise "banned 3 times" means nothing.</summary>
    [Fact]
    public async Task A_ban_and_its_lifting_are_both_recorded_newest_first()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modlog-lift");

        await Communities.BannedAsync(owner, communityId, troll.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);
        // Unbanning someone who is not banned is a no-op, not a decision.
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);

        var log = await CommunityLogAsync(owner, communityId);
        Assert.Equal(new[] { "COMMUNITY_BAN_LIFTED", "COMMUNITY_BAN_ISSUED" }, Actions(log));
        Assert.Equal(JsonValueKind.Null, log[0].GetProperty("expiresAt").ValueKind);
    }

    /// <summary>Removing an expired ban is clearing a dead row, not a decision — there was nothing to lift.</summary>
    [Fact]
    public async Task Removing_an_expired_ban_is_not_recorded_as_lifting_it()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modlog-dead-ban");
        await Communities.BannedAsync(owner, communityId, troll.Id, durationDays: 1);
        await Communities.ExpireBanAsync(communityId, troll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);

        Assert.Equal(new[] { "COMMUNITY_BAN_ISSUED" }, Actions(await CommunityLogAsync(owner, communityId)));
    }

    /// <summary>
    /// The point of original ADR 0019 and 0027: the number of reports is written when the post is removed — after
    /// that there is nothing to count, its reports go with it — and every reporter gets an upheld entry, so the
    /// log knows <i>whose</i> reports were right.
    /// </summary>
    [Fact]
    public async Task A_removed_post_records_its_report_count_and_an_upheld_report_per_reporter()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modlog-removed");
        var first = await factory.CreateLoggedInUserAsync(_client, "modlog-removed-reporter1");
        var second = await factory.CreateLoggedInUserAsync(_client, "modlog-removed-reporter2");
        var postId = await Content.PublishedInAsync(author, communityId, "sporny post");
        await _reports.ReportedAsync(first, postId);
        await _reports.ReportedAsync(second, postId);

        await _reports.RemovedAsync(owner, postId);

        var log = await CommunityLogAsync(owner, communityId);
        var removal = Assert.Single(log, e => Action(e) == "COMMUNITY_POST_REMOVED");
        Assert.Equal(author.Id, removal.GetProperty("subjectId").GetGuid());
        Assert.Equal(owner.Id, removal.GetProperty("actorId").GetGuid());
        Assert.Equal(2, removal.GetProperty("reportCount").GetInt32());
        var upheld = log.Where(e => Action(e) == "POST_REPORT_UPHELD").ToList();
        Assert.Equal(
            new[] { first.Id, second.Id }.Order(),
            upheld.Select(e => e.GetProperty("subjectId").GetGuid()).Order());
        Assert.All(upheld, e => Assert.Equal(owner.Id, e.GetProperty("actorId").GetGuid()));
        Assert.All(upheld, e => Assert.Equal(JsonValueKind.Null, e.GetProperty("reportCount").ValueKind));
    }

    /// <summary>Deleting one's own post is not a moderation decision, so the history has nothing to say about it.</summary>
    [Fact]
    public async Task Deleting_ones_own_post_is_not_recorded()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modlog-own");
        var postId = await Content.PublishedInAsync(author, communityId, "sam usuwam");

        Assert.Equal(HttpStatusCode.NoContent, (await Content.DeletePostAsync(author, postId)).StatusCode);

        Assert.Empty(await CommunityLogAsync(owner, communityId));
    }

    /// <summary>The subject of a dismissal is the reporter, not the post's author — it is their report that was decided.</summary>
    [Fact]
    public async Task A_dismissed_report_is_recorded_about_the_reporter()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modlog-dismissed");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "modlog-dismissed-reporter");
        await _reports.ReportedAsync(reporter, await Content.PublishedInAsync(author, communityId));

        await _reports.DismissAllAsync(owner, communityId);

        var entry = Assert.Single(await CommunityLogAsync(owner, communityId));
        Assert.Equal("POST_REPORT_DISMISSED", Action(entry));
        Assert.Equal(reporter.Id, entry.GetProperty("subjectId").GetGuid());
        Assert.Equal(owner.Id, entry.GetProperty("actorId").GetGuid());
    }

    [Fact]
    public async Task A_comment_removed_by_a_moderator_is_recorded_about_its_author()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modlog-comment");
        var postId = await Content.PublishedInAsync(author, communityId);
        var commentId = await Content.CommentedAsync(author, postId, "trolling");

        Assert.Equal(HttpStatusCode.NoContent, (await Content.DeleteCommentAsync(owner, postId, commentId)).StatusCode);

        var entry = Assert.Single(await CommunityLogAsync(owner, communityId));
        Assert.Equal("COMMUNITY_COMMENT_REMOVED", Action(entry));
        Assert.Equal(author.Id, entry.GetProperty("subjectId").GetGuid());
        Assert.Equal(owner.Id, entry.GetProperty("actorId").GetGuid());
    }

    /// <summary>
    /// Appeals and their outcomes — what original ADR 0017 and 0023 found missing. The appeal is the one decision
    /// here the punished person makes, so they are its actor; accepting it lifts the ban, recorded as well.
    /// </summary>
    [Fact]
    public async Task An_appeal_is_recorded_on_both_sides()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modlog-appeal");
        await Communities.BannedAsync(owner, communityId, troll.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.AppealAsync(troll, communityId)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);

        var log = await CommunityLogAsync(owner, communityId);
        Assert.Equal(
            new[] { "COMMUNITY_BAN_APPEAL_ACCEPTED", "COMMUNITY_BAN_APPEAL_SUBMITTED", "COMMUNITY_BAN_ISSUED", "COMMUNITY_BAN_LIFTED" },
            Actions(log).Order());
        Assert.Equal(troll.Id, Assert.Single(log, e => Action(e) == "COMMUNITY_BAN_APPEAL_SUBMITTED").GetProperty("actorId").GetGuid());
        Assert.Equal(owner.Id, Assert.Single(log, e => Action(e) == "COMMUNITY_BAN_APPEAL_ACCEPTED").GetProperty("actorId").GetGuid());
    }

    /// <summary>
    /// The second instance (original ADR 0028): the decision has the administrator as its actor, so the history
    /// shows it came from outside the community — and an overturn lifts the ban like any other.
    /// </summary>
    [Fact]
    public async Task An_overturned_escalation_is_recorded_with_the_admin_as_the_actor()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-overturn-admin");
        var (owner, troll, communityId) = await EscalatedAsync("modlog-overturn");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}", admin.Token)).StatusCode);

        var log = await CommunityLogAsync(owner, communityId);
        Assert.Equal(
            new[] { "COMMUNITY_BAN_APPEAL_ESCALATED", "COMMUNITY_BAN_APPEAL_REJECTED", "COMMUNITY_BAN_APPEAL_SUBMITTED", "COMMUNITY_BAN_ESCALATION_ACCEPTED", "COMMUNITY_BAN_ISSUED", "COMMUNITY_BAN_LIFTED" },
            Actions(log).Order());
        Assert.Equal(troll.Id, Assert.Single(log, e => Action(e) == "COMMUNITY_BAN_APPEAL_ESCALATED").GetProperty("actorId").GetGuid());
        Assert.Equal(admin.Id, Assert.Single(log, e => Action(e) == "COMMUNITY_BAN_ESCALATION_ACCEPTED").GetProperty("actorId").GetGuid());
        Assert.Equal(admin.Id, Assert.Single(log, e => Action(e) == "COMMUNITY_BAN_LIFTED").GetProperty("actorId").GetGuid());
    }

    [Fact]
    public async Task A_rejected_escalation_is_recorded_and_the_ban_stays()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-final-admin");
        var (owner, troll, communityId) = await EscalatedAsync("modlog-final");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}/escalation-rejection", new { }, admin.Token)).StatusCode);

        var log = await CommunityLogAsync(owner, communityId);
        Assert.Equal("COMMUNITY_BAN_ESCALATION_REJECTED", Action(log[0]));
        Assert.Equal(admin.Id, log[0].GetProperty("actorId").GetGuid());
        Assert.DoesNotContain("COMMUNITY_BAN_LIFTED", Actions(log));
    }

    /// <summary>Only an actual change of the role is recorded (original ADR 0016) — the gate sits at the publisher.</summary>
    [Fact]
    public async Task Granting_and_revoking_the_moderator_role_are_recorded()
    {
        var (owner, member, communityId) = await _reports.CommunityWithAuthorAsync("modlog-role");

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, member.Id, "MODERATOR")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, member.Id, "MODERATOR")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, member.Id, "MEMBER")).StatusCode);

        var log = await CommunityLogAsync(owner, communityId);
        Assert.Equal(new[] { "COMMUNITY_MODERATOR_REVOKED", "COMMUNITY_MODERATOR_GRANTED" }, Actions(log));
        Assert.All(log, e => Assert.Equal(member.Id, e.GetProperty("subjectId").GetGuid()));
        Assert.All(log, e => Assert.Equal(owner.Id, e.GetProperty("actorId").GetGuid()));
    }

    /// <summary>
    /// The platform history answers a question nothing could answer before — "what does this person have behind
    /// them" — and joins community decisions with platform ones.
    /// </summary>
    [Fact]
    public async Task The_platform_history_spans_community_and_platform_decisions()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-platform-admin");
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modlog-platform");
        await Communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{troll.Id}/suspension", new { reason = "nękanie", durationDays = 7 }, admin.Token)).StatusCode);

        var log = await PlatformLogAsync(admin, troll.Id);
        Assert.Equal(new[] { "ACCOUNT_SUSPENDED", "COMMUNITY_BAN_ISSUED" }, Actions(log));
        var suspension = log[0];
        Assert.Equal(JsonValueKind.Null, suspension.GetProperty("communityId").ValueKind);
        Assert.Equal(admin.Id, suspension.GetProperty("actorId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, suspension.GetProperty("expiresAt").ValueKind);

        // A platform decision belongs to no community, so it is not in the community's history.
        Assert.Equal(new[] { "COMMUNITY_BAN_ISSUED" }, Actions(await CommunityLogAsync(owner, communityId)));
    }

    /// <summary>
    /// The suspension appeal is a copy of the ban appeal (original ADR 0023), in the history as well — with one
    /// difference the publisher makes: an accepted appeal <b>replaces</b> the lifting (one message to the
    /// appellant, saying why), so the acceptance is the entry that says the suspension ended.
    /// </summary>
    [Fact]
    public async Task A_suspension_appeal_and_its_acceptance_are_recorded()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-suspension-admin");
        var user = await factory.CreateLoggedInUserAsync(_client, "modlog-suspension");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{user.Id}/suspension", new { reason = "spam" }, admin.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/v1/auth/me/suspension/appeal", new { text = "pomyłka" }, user.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{user.Id}/suspension", admin.Token)).StatusCode);

        var log = await PlatformLogAsync(admin, user.Id);
        Assert.Equal(new[] { "SUSPENSION_APPEAL_ACCEPTED", "SUSPENSION_APPEAL_SUBMITTED", "ACCOUNT_SUSPENDED" }, Actions(log));
        Assert.Equal(user.Id, Assert.Single(log, e => Action(e) == "SUSPENSION_APPEAL_SUBMITTED").GetProperty("actorId").GetGuid());
        Assert.Equal(admin.Id, Assert.Single(log, e => Action(e) == "SUSPENSION_APPEAL_ACCEPTED").GetProperty("actorId").GetGuid());
        Assert.Equal(JsonValueKind.Null, Assert.Single(log, e => Action(e) == "ACCOUNT_SUSPENDED").GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task A_rejected_suspension_appeal_is_recorded()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-rejected-admin");
        var user = await factory.CreateLoggedInUserAsync(_client, "modlog-rejected");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{user.Id}/suspension", new { reason = "spam" }, admin.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/v1/auth/me/suspension/appeal", new { text = "pomyłka" }, user.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync($"/api/v1/admin/users/{user.Id}/suspension/appeal/rejection", new { }, admin.Token)).StatusCode);

        Assert.Equal(new[] { "SUSPENSION_APPEAL_REJECTED", "SUSPENSION_APPEAL_SUBMITTED", "ACCOUNT_SUSPENDED" }, Actions(await PlatformLogAsync(admin, user.Id)));
    }

    /// <summary>
    /// The point of original ADR 0031: granting and revoking the administrator role reach the history. Before, they
    /// left only a log line and a counter — traces that need access to the machine, unlike any sanction against
    /// an ordinary account.
    /// </summary>
    [Fact]
    public async Task Admin_role_changes_are_recorded_as_platform_decisions()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-promoter");
        var promoted = await factory.CreateLoggedInUserAsync(_client, "modlog-promoted");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{promoted.Id}/admin", new { }, admin.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{promoted.Id}/admin", admin.Token)).StatusCode);

        var log = await PlatformLogAsync(admin, promoted.Id);
        Assert.Equal(new[] { "PLATFORM_ADMIN_REVOKED", "PLATFORM_ADMIN_GRANTED" }, Actions(log));
        var revocation = log[0];
        Assert.Equal(JsonValueKind.Null, revocation.GetProperty("communityId").ValueKind);
        Assert.Equal(promoted.Id, revocation.GetProperty("subjectId").GetGuid());
        Assert.Equal(admin.Id, revocation.GetProperty("actorId").GetGuid());
        Assert.Equal(JsonValueKind.Null, revocation.GetProperty("expiresAt").ValueKind);
    }

    /// <summary>
    /// "Only an actual change" sits at the publisher (original ADR 0021, 0031), so the history inherits it for
    /// free: granting the role to someone who has it changes nothing, and there is nothing to record.
    /// </summary>
    [Fact]
    public async Task A_repeated_grant_does_not_add_a_second_entry()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-idempotent-admin");
        var promoted = await factory.CreateLoggedInUserAsync(_client, "modlog-idempotent");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{promoted.Id}/admin", new { }, admin.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{promoted.Id}/admin", new { }, admin.Token)).StatusCode);

        Assert.Equal(new[] { "PLATFORM_ADMIN_GRANTED" }, Actions(await PlatformLogAsync(admin, promoted.Id)));
    }

    /// <summary>
    /// A community's history is for its moderators, not any member; the unknown community is checked first, or a
    /// 403 would suggest something is there. The platform history is the administrators' (by the path).
    /// </summary>
    [Fact]
    public async Task The_community_history_is_for_its_moderators_and_the_platform_history_for_admins()
    {
        var (owner, member, communityId) = await _reports.CommunityWithAuthorAsync("modlog-permissions");

        await (await _client.GetAsync($"/api/v1/communities/{communityId}/moderation-log", member.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MODERATOR");
        var notFound = await (await _client.GetAsync($"/api/v1/communities/{Guid.NewGuid()}/moderation-log", owner.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
        Assert.Equal("Nie ma takiej społeczności", notFound.GetProperty("message").GetString());
        await (await _client.GetAsync("/api/v1/admin/moderation-log", owner.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, member.Id, "MODERATOR")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/v1/communities/{communityId}/moderation-log", member.Token)).StatusCode);
    }

    /// <summary>
    /// Deleting an account takes the entries <b>about</b> the person and leaves the entries <b>by</b> them without
    /// an author — the asymmetry of <c>banned_by</c> (original ADR 0011).
    /// </summary>
    /// <remarks>
    /// A <b>moderator</b> bans here, not the owner, and that is not cosmetic: the owner's account would take the
    /// whole community with it, and its history — there would be nothing left to orphan.
    /// </remarks>
    [Fact]
    public async Task Account_deletion_removes_the_entries_about_the_person_and_orphans_the_ones_by_them()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-deletion-admin");
        var (owner, moderator, communityId) = await _reports.CommunityWithAuthorAsync("modlog-deletion");
        var troll = await factory.CreateLoggedInUserAsync(_client, "modlog-deletion-troll");
        await Communities.JoinedAsync(troll, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        await Communities.BannedAsync(moderator, communityId, troll.Id);

        // The moderator leaves: the decision stays, it only loses its author.
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.DeleteAccountAsync(moderator)).StatusCode);
        var ban = Assert.Single(await PlatformLogAsync(admin, troll.Id));
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("actorId").ValueKind);
        Assert.Empty(await PlatformLogAsync(admin, moderator.Id));

        // The banned person leaves: the history of sanctions against them goes with them.
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.DeleteAccountAsync(troll)).StatusCode);
        Assert.Empty(await PlatformLogAsync(admin, troll.Id));
    }

    /// <summary>
    /// A community's history goes with it — in practice also when its owner deletes the account, which deletes
    /// their communities.
    /// </summary>
    [Fact]
    public async Task A_deleted_community_takes_its_history_with_it()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-community-admin");
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modlog-community");
        await Communities.BannedAsync(owner, communityId, troll.Id);
        Assert.NotEmpty(await PlatformLogAsync(admin, troll.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}", owner.Token)).StatusCode);

        Assert.Empty(await PlatformLogAsync(admin, troll.Id));
    }

    [Fact]
    public async Task An_owners_account_deletion_takes_the_history_of_their_communities_with_it()
    {
        var admin = await factory.CreateAdminAsync(_client, "modlog-owner-admin");
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modlog-owner");
        await Communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.DeleteAccountAsync(owner)).StatusCode);

        Assert.Empty(await PlatformLogAsync(admin, troll.Id));
    }

    private async Task<(LoggedInUser Owner, LoggedInUser Troll, Guid CommunityId)> EscalatedAsync(string prefix)
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync(prefix);
        await Communities.BannedAsync(owner, communityId, troll.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.AppealAsync(troll, communityId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.RejectAppealAsync(owner, communityId, troll.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.EscalateAsync(troll, communityId)).StatusCode);
        return (owner, troll, communityId);
    }

    private async Task<IReadOnlyList<JsonElement>> CommunityLogAsync(LoggedInUser moderator, Guid communityId) =>
        [.. (await (await _client.GetAsync($"/api/v1/communities/{communityId}/moderation-log", moderator.Token)).ReadJsonAsync()).GetProperty("content").EnumerateArray()];

    private async Task<IReadOnlyList<JsonElement>> PlatformLogAsync(LoggedInUser admin, Guid userId) =>
        [.. (await (await _client.GetAsync($"/api/v1/admin/moderation-log?userId={userId}", admin.Token)).ReadJsonAsync()).GetProperty("content").EnumerateArray()];

    private static string Action(JsonElement entry) => entry.GetProperty("action").GetString()!;

    private static string[] Actions(IEnumerable<JsonElement> log) => [.. log.Select(Action)];
}
