using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// Notifications of moderation decisions (original ADR 0012). Ported from the original
/// <c>ModerationNotificationIntegrationTest</c> and the inbox parts of <c>BanAppealIntegrationTest</c>,
/// <c>BanAppealEscalationIntegrationTest</c> and <c>CommentModerationIntegrationTest</c>.
/// </summary>
/// <remarks>
/// ADR 0011 gave the tools but left both sides in the dark: the banned user learnt of the ban only when
/// trying to write something — a decision looking like a failure — the reporter got no answer, and the
/// queue worked only when a moderator happened to look into it.
/// </remarks>
public sealed class ModerationNotificationTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly NotificationTestClient _inbox = new(factory.CreateClient());
    private readonly ReportTestClient _reports = new(factory, factory.CreateClient());

    private CommunityTestClient Communities => _reports.Communities;

    private ContentTestClient Content => _reports.Content;

    /// <summary>The banned user learns of the ban at once, not when trying to write. The decision is the community's, so no name or avatar.</summary>
    [Fact]
    public async Task The_banned_user_is_notified_without_the_moderators_name()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-ban");

        await Communities.BannedAsync(owner, communityId, troll.Id);

        var ban = Assert.Single(await _inbox.ListAsync(troll));
        Assert.Equal("COMMUNITY_BANNED", ban.GetProperty("type").GetString());
        Assert.Equal(communityId, ban.GetProperty("resourceId").GetGuid());
        Assert.Equal(owner.Id, ban.GetProperty("actorId").GetGuid());
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("actorDisplayName").ValueKind);
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("actorAvatarUrl").ValueKind);
    }

    /// <summary>A ban is idempotent, so a moderator's repeated click must not add notifications about the same thing.</summary>
    [Fact]
    public async Task A_repeated_ban_does_not_repeat_the_notification()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-reban");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Communities.BannedAsync(owner, communityId, troll.Id);
        }

        Assert.Single(await _inbox.OfTypeAsync(troll, "COMMUNITY_BANNED"));
    }

    /// <summary>"Only a new ban" means a change from not banned to banned (original ADR 0014): a ban over an expired one is new, and tells again.</summary>
    [Fact]
    public async Task A_ban_over_an_expired_one_notifies_again()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-rebanned");
        await Communities.BannedAsync(owner, communityId, troll.Id);
        await Communities.ExpireBanAsync(communityId, troll.Id);

        await Communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(2, (await _inbox.OfTypeAsync(troll, "COMMUNITY_BANNED")).Count);
    }

    [Fact]
    public async Task The_author_is_told_when_a_moderator_removes_their_post_but_not_when_they_remove_it_themselves()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-rm");

        Assert.Equal(HttpStatusCode.NoContent, (await Content.DeletePostAsync(author, await Content.PublishedInAsync(author, communityId, "sam usuwam"))).StatusCode);
        Assert.Empty(await _inbox.OfTypeAsync(author, "COMMUNITY_POST_REMOVED"));

        Assert.Equal(HttpStatusCode.NoContent, (await Content.DeletePostAsync(owner, await Content.PublishedInAsync(author, communityId, "usuwa moderator"))).StatusCode);

        var removed = Assert.Single(await _inbox.OfTypeAsync(author, "COMMUNITY_POST_REMOVED"));
        Assert.Equal(communityId, removed.GetProperty("resourceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("actorDisplayName").ValueKind);
    }

    /// <summary>
    /// A removed comment, told to its author. The same moderator also leaves an ordinary trace with the
    /// author — a like — so the moderator's name is on the same inbox page, and must not leak into the
    /// decision: the anonymity is decided per row, not by who is in the page's names.
    /// </summary>
    [Fact]
    public async Task A_removed_comment_is_told_without_the_moderators_name_even_next_to_their_ordinary_activity()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-comment");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "modnotif-comment-moderator");
        await Communities.JoinedAsync(moderator, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        var troll = await factory.CreateLoggedInUserAsync(_client, "modnotif-comment-troll");
        var postId = await Content.PublishedInAsync(author, communityId);
        var commentId = await Content.CommentedAsync(troll, postId);
        await Content.LikeAsync(moderator, await Content.PublishAsync(troll, "post trolla"));

        Assert.Equal(HttpStatusCode.NoContent, (await Content.DeleteCommentAsync(moderator, postId, commentId)).StatusCode);

        var inbox = await _inbox.ListAsync(troll);
        var removal = inbox[0];
        Assert.Equal("COMMUNITY_COMMENT_REMOVED", removal.GetProperty("type").GetString());
        Assert.Equal(postId, removal.GetProperty("resourceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, removal.GetProperty("actorDisplayName").ValueKind);
        Assert.Equal(JsonValueKind.Null, removal.GetProperty("actorAvatarUrl").ValueKind);
        var like = inbox[1];
        Assert.Equal("POST_LIKED", like.GetProperty("type").GetString());
        Assert.Equal(NameOf(moderator), like.GetProperty("actorDisplayName").GetString());
    }

    /// <summary>Deleting one's own comment is no moderation decision.</summary>
    [Fact]
    public async Task Removing_ones_own_comment_tells_nobody()
    {
        var (_, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-own-comment");
        var postId = await Content.PublishedInAsync(author, communityId);
        var commentId = await Content.CommentedAsync(author, postId);

        Assert.Equal(HttpStatusCode.NoContent, (await Content.DeleteCommentAsync(author, postId, commentId)).StatusCode);

        Assert.Empty(await _inbox.OfTypeAsync(author, "COMMUNITY_COMMENT_REMOVED"));
    }

    /// <summary>
    /// The half of moderation the queue does not do by itself: a queue needs someone to look into it, a
    /// notification says there is a reason to. <b>Every</b> moderator gets it, the author of the post does
    /// not, and a second report of the same post does not call them again — the count in the queue does.
    /// </summary>
    [Fact]
    public async Task All_moderators_are_told_of_the_first_report()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-rep");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "modnotif-rep-mod");
        await Communities.JoinedAsync(moderator, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        var postId = await Content.PublishedInAsync(author, communityId, "sporny post");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "modnotif-rep-reporter");

        await _reports.ReportedAsync(reporter, postId);

        var reported = Assert.Single(await _inbox.OfTypeAsync(owner, "COMMUNITY_POST_REPORTED"));
        Assert.Equal(postId, reported.GetProperty("resourceId").GetGuid());
        Assert.Equal(reporter.Id, reported.GetProperty("actorId").GetGuid());
        Assert.Single(await _inbox.OfTypeAsync(moderator, "COMMUNITY_POST_REPORTED"));
        Assert.Empty(await _inbox.OfTypeAsync(author, "COMMUNITY_POST_REPORTED"));

        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modnotif-rep-reporter2"), postId);
        Assert.Single(await _inbox.OfTypeAsync(owner, "COMMUNITY_POST_REPORTED"));
    }

    /// <summary>A moderator who reports a post in their own community is not told of their own report.</summary>
    [Fact]
    public async Task A_reporting_moderator_is_not_told_of_their_own_report()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-self-report");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "modnotif-self-report-mod");
        await Communities.JoinedAsync(moderator, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);

        await _reports.ReportedAsync(moderator, await Content.PublishedInAsync(author, communityId));

        Assert.Single(await _inbox.OfTypeAsync(owner, "COMMUNITY_POST_REPORTED"));
        Assert.Empty(await _inbox.OfTypeAsync(moderator, "COMMUNITY_POST_REPORTED"));
    }

    [Fact]
    public async Task The_reporter_is_told_when_their_report_is_dismissed_once()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-dis");
        var postId = await Content.PublishedInAsync(author, communityId, "post do zgloszenia");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "modnotif-dis-reporter");
        await _reports.ReportedAsync(reporter, postId);
        var reportId = (await _reports.QueueAsync(owner, communityId))[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.DismissAsync(owner, communityId, reportId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _reports.DismissAsync(owner, communityId, reportId)).StatusCode);

        var dismissed = Assert.Single(await _inbox.OfTypeAsync(reporter, "POST_REPORT_DISMISSED"));
        Assert.Equal(postId, dismissed.GetProperty("resourceId").GetGuid());
    }

    /// <summary>
    /// "The first report" means the first in the queue (original ADR 0049). After the earlier reports were
    /// dismissed the queue is empty for this post, so a new report must call the moderators again — before,
    /// it waited there without a signal.
    /// </summary>
    [Fact]
    public async Task A_report_after_a_dismissal_calls_the_moderators_again()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-redo");
        var postId = await Content.PublishedInAsync(author, communityId, "post zgloszony dwa razy");
        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modnotif-redo-reporter1"), postId);
        await _reports.DismissAllAsync(owner, communityId);

        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modnotif-redo-reporter2"), postId);

        Assert.Equal(2, (await _inbox.OfTypeAsync(owner, "COMMUNITY_POST_REPORTED")).Count);
    }

    /// <summary>
    /// The heart of it from the safety side: a moderation notification <b>gets through a block</b>. The
    /// rest of the inbox disappears when the actor is blocked — as it should — but a moderation decision
    /// cannot be declined: under that filter, blocking the moderator would be enough never to see one's
    /// own ban.
    /// </summary>
    [Fact]
    public async Task A_moderation_notification_survives_blocking_the_moderator()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-block");

        // An ordinary notification from the same person, for contrast.
        await Content.LikeAsync(owner, await Content.PublishAsync(troll, "zwykly post"));
        Assert.Single(await _inbox.OfTypeAsync(troll, "POST_LIKED"));
        await Communities.BannedAsync(owner, communityId, troll.Id);

        await Content.BlockAsync(troll, owner);

        Assert.Empty(await _inbox.OfTypeAsync(troll, "POST_LIKED"));
        Assert.Single(await _inbox.OfTypeAsync(troll, "COMMUNITY_BANNED"));
    }

    /// <summary>
    /// The other kind in the set: a message that starts the moderation loop. A moderator who blocked a troll
    /// still sees the troll's report — otherwise the queue would quietly die while looking alive.
    /// </summary>
    [Fact]
    public async Task A_report_reaches_a_moderator_who_blocked_the_reporter()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-block-report");
        var troll = await factory.CreateLoggedInUserAsync(_client, "modnotif-block-report-troll");
        await Content.BlockAsync(owner, troll);

        await _reports.ReportedAsync(troll, await Content.PublishedInAsync(author, communityId));

        Assert.Single(await _inbox.OfTypeAsync(owner, "COMMUNITY_POST_REPORTED"));
    }

    /// <summary>The badge counts what the list shows, moderation decisions through a block included — otherwise it would show a number nothing brings down.</summary>
    [Fact]
    public async Task The_unread_count_matches_what_is_visible()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-count");
        await Communities.BannedAsync(owner, communityId, troll.Id);
        await Content.BlockAsync(troll, owner);

        Assert.Equal(1, await _inbox.UnreadCountAsync(troll));

        await _inbox.ReadAllAsync(troll);

        Assert.Equal(0, await _inbox.UnreadCountAsync(troll));
    }

    /// <summary>An appeal calls every moderator, once per ban; a new ban gives a new right to appeal, and a new call.</summary>
    [Fact]
    public async Task An_appeal_calls_every_moderator_once_per_ban()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-appeal");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "modnotif-appeal-mod");
        await Communities.JoinedAsync(moderator, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        await Communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.AppealAsync(troll, communityId)).StatusCode);
        await (await Communities.AppealAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Conflict, "BAN_APPEAL_ALREADY_SUBMITTED");

        var appeal = Assert.Single(await _inbox.OfTypeAsync(owner, "BAN_APPEAL_SUBMITTED"));
        Assert.Equal(troll.Id, appeal.GetProperty("actorId").GetGuid());
        Assert.Equal(communityId, appeal.GetProperty("resourceId").GetGuid());
        Assert.Single(await _inbox.OfTypeAsync(moderator, "BAN_APPEAL_SUBMITTED"));

        await Communities.ExpireBanAsync(communityId, troll.Id);
        await Communities.BannedAsync(owner, communityId, troll.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.AppealAsync(troll, communityId)).StatusCode);
        Assert.Equal(2, (await _inbox.OfTypeAsync(owner, "BAN_APPEAL_SUBMITTED")).Count);
    }

    /// <summary>Lifting a ban with a pending appeal is accepting the appeal — and the appellant is told so.</summary>
    [Fact]
    public async Task Lifting_a_ban_with_a_pending_appeal_tells_the_appellant_it_was_accepted()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-accept");
        await Communities.BannedAsync(owner, communityId, troll.Id);
        await Communities.AppealAsync(troll, communityId);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);

        Assert.Equal(JsonValueKind.Null, Assert.Single(await _inbox.OfTypeAsync(troll, "BAN_APPEAL_ACCEPTED")).GetProperty("actorDisplayName").ValueKind);
    }

    /// <summary>Without an appeal there is nothing to accept: lifting the ban answers no petition.</summary>
    [Fact]
    public async Task Lifting_a_ban_without_an_appeal_accepts_nothing()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-plain-unban");
        await Communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);

        Assert.Empty(await _inbox.OfTypeAsync(troll, "BAN_APPEAL_ACCEPTED"));
    }

    /// <summary>A rejection reaches the appellant even across a block of the moderator: one cannot decline an answer one asked for.</summary>
    [Fact]
    public async Task A_rejection_reaches_the_appellant_even_across_a_block()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-reject");
        await Communities.BannedAsync(owner, communityId, troll.Id);
        await Communities.AppealAsync(troll, communityId);
        await Content.BlockAsync(troll, owner);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.RejectAppealAsync(owner, communityId, troll.Id)).StatusCode);

        Assert.Single(await _inbox.OfTypeAsync(troll, "BAN_APPEAL_REJECTED"));
    }

    /// <summary>The second instance answers the appellant both ways (original ADR 0028), signed by nobody: the platform decided, not a person.</summary>
    [Fact]
    public async Task The_second_instance_answers_the_appellant()
    {
        var admin = await factory.CreateAdminAsync(_client, "modnotif-escalation-admin");
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modnotif-overturn");
        await EscalatedAsync(owner, troll, communityId);
        var (otherOwner, otherTroll, otherCommunityId) = await _reports.CommunityWithAuthorAsync("modnotif-kept");
        await EscalatedAsync(otherOwner, otherTroll, otherCommunityId);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}", admin.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync($"/api/v1/admin/communities/{otherCommunityId}/bans/{otherTroll.Id}/escalation-rejection", new { }, admin.Token)).StatusCode);

        var overturned = Assert.Single(await _inbox.OfTypeAsync(troll, "BAN_ESCALATION_ACCEPTED"));
        Assert.Equal(admin.Id, overturned.GetProperty("actorId").GetGuid());
        Assert.Equal(communityId, overturned.GetProperty("resourceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, overturned.GetProperty("actorDisplayName").ValueKind);
        var kept = Assert.Single(await _inbox.OfTypeAsync(otherTroll, "BAN_ESCALATION_REJECTED"));
        Assert.Equal(admin.Id, kept.GetProperty("actorId").GetGuid());
    }

    private async Task EscalatedAsync(LoggedInUser owner, LoggedInUser troll, Guid communityId)
    {
        await Communities.BannedAsync(owner, communityId, troll.Id);
        await Communities.AppealAsync(troll, communityId);
        await Communities.RejectAppealAsync(owner, communityId, troll.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.EscalateAsync(troll, communityId)).StatusCode);
    }

    private static string NameOf(LoggedInUser user) => user.User.Email[..user.User.Email.IndexOf('@', StringComparison.Ordinal)];
}
