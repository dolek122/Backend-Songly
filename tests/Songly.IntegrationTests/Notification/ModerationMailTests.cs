using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Songly.Common.Mail;
using Songly.IntegrationTests.Content;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// E-mail as the second channel for moderation decisions (original ADR 0013). Ported from the original
/// <c>ModerationMailIntegrationTest</c> and the scenarios of <c>ModerationNotificationMailerTest</c>, which
/// the original ran on mocks — here they go through the API.
/// </summary>
/// <remarks>
/// The inbox is <see cref="ModerationNotificationTests"/>' business; these check only what went out by
/// mail. Registration sends mail too, so every case clears the inbox it asserts first.
/// </remarks>
public sealed class ModerationMailTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ReportTestClient _reports = new(factory, factory.CreateClient());

    private RecordingMailSender Mails => factory.Mails;

    [Fact]
    public async Task The_banned_user_gets_a_mail_naming_the_community_and_the_term()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modmail-ban");
        Mails.ClearFor(troll.User.Email);

        await _reports.Communities.BannedAsync(owner, communityId, troll.Id, durationDays: 7);

        var mail = Mails.LastTo(troll.User.Email);
        var name = await CommunityNameAsync(owner, communityId);
        Assert.Contains("ban", mail.Subject, StringComparison.Ordinal);
        Assert.Contains(name, mail.Subject, StringComparison.Ordinal);
        Assert.Contains(name, mail.Body, StringComparison.Ordinal);
        Assert.Contains("wygasa za 7 dni", mail.Body, StringComparison.Ordinal);

        // The reason stays in the pull channel (original ADR 0015): mail is pushed past every block.
        Assert.DoesNotContain("spam", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ban_without_a_term_says_so()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modmail-forever");
        Mails.ClearFor(troll.User.Email);

        await _reports.Communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Contains("Ban jest bezterminowy.", Mails.LastTo(troll.User.Email).Body, StringComparison.Ordinal);
    }

    /// <summary>"Only a new ban" sits at the publisher, so it holds for both channels — the mail inherits it like the inbox.</summary>
    [Fact]
    public async Task A_repeated_ban_does_not_repeat_the_mail()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modmail-reban");
        Mails.ClearFor(troll.User.Email);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await _reports.Communities.BannedAsync(owner, communityId, troll.Id);
        }

        Assert.Single(Mails.SentTo(troll.User.Email));
    }

    [Fact]
    public async Task The_author_gets_a_mail_only_when_someone_else_removes_their_post()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-rm");
        var own = await _reports.Content.PublishedInAsync(author, communityId, "sam usuwam");
        var moderated = await _reports.Content.PublishedInAsync(author, communityId, "usuwa moderator");
        Mails.ClearFor(author.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Content.DeletePostAsync(author, own)).StatusCode);
        Assert.Empty(Mails.SentTo(author.User.Email));

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Content.DeletePostAsync(owner, moderated)).StatusCode);
        var mail = Mails.LastTo(author.User.Email);
        Assert.Contains("usunięty", mail.Subject, StringComparison.Ordinal);
        Assert.Contains(await CommunityNameAsync(owner, communityId), mail.Subject, StringComparison.Ordinal);
    }

    /// <summary>The comment's counterpart — and the publisher-side gate the inbox could not show: removing one's own comment sends nothing.</summary>
    [Fact]
    public async Task The_author_gets_a_mail_only_when_someone_else_removes_their_comment()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-comment");
        var postId = await _reports.Content.PublishedInAsync(author, communityId);
        var own = await _reports.Content.CommentedAsync(author, postId, "sam usuwam");
        var moderated = await _reports.Content.CommentedAsync(author, postId, "usuwa moderator");
        Mails.ClearFor(author.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Content.DeleteCommentAsync(author, postId, own)).StatusCode);
        Assert.Empty(Mails.SentTo(author.User.Email));

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Content.DeleteCommentAsync(owner, postId, moderated)).StatusCode);
        Assert.Contains("komentarz", Assert.Single(Mails.SentTo(author.User.Email)).Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of ADR 0013: the report mail reaches moderators who do not open the app. Neither the
    /// author nor the reporter gets one, and a second report of the same post sends nothing.
    /// </summary>
    [Fact]
    public async Task The_first_report_mails_all_moderators_once()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-rep");
        var moderator = await ModeratorAsync(owner, communityId, "modmail-rep-mod");
        var postId = await _reports.Content.PublishedInAsync(author, communityId, "sporny post");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "modmail-rep-reporter");
        Clear(owner, moderator, author, reporter);

        await _reports.ReportedAsync(reporter, postId);

        Assert.Contains("zgłoszenie", Mails.LastTo(owner.User.Email).Subject, StringComparison.Ordinal);
        Assert.Contains(await CommunityNameAsync(owner, communityId), Mails.LastTo(owner.User.Email).Subject, StringComparison.Ordinal);
        Assert.Contains("zgłoszenie", Mails.LastTo(moderator.User.Email).Subject, StringComparison.Ordinal);
        Assert.Empty(Mails.SentTo(author.User.Email));
        Assert.Empty(Mails.SentTo(reporter.User.Email));

        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modmail-rep-reporter2"), postId);
        Assert.Single(Mails.SentTo(owner.User.Email));
        Assert.Single(Mails.SentTo(moderator.User.Email));
    }

    /// <summary>A moderator reporting a post in their own community is not mailed about their own report.</summary>
    [Fact]
    public async Task A_reporting_moderator_is_not_mailed_about_their_own_report()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-self");
        var moderator = await ModeratorAsync(owner, communityId, "modmail-self-mod");
        Clear(owner, moderator);

        await _reports.ReportedAsync(moderator, await _reports.Content.PublishedInAsync(author, communityId));

        Assert.Single(Mails.SentTo(owner.User.Email));
        Assert.Empty(Mails.SentTo(moderator.User.Email));
    }

    /// <summary>A suspended moderator cannot handle the case (original ADR 0022) — calls to the queue go only to those who can open it.</summary>
    [Fact]
    public async Task A_suspended_moderator_is_left_out_of_queue_mail()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-suspended");
        var moderator = await ModeratorAsync(owner, communityId, "modmail-suspended-mod");
        await factory.SuspendAsync(moderator.User);
        Clear(owner, moderator);

        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modmail-suspended-reporter"), await _reports.Content.PublishedInAsync(author, communityId));

        Assert.Single(Mails.SentTo(owner.User.Email));
        Assert.Empty(Mails.SentTo(moderator.User.Email));
    }

    /// <summary>
    /// The interval of ADR 0018: within it the second mail to a community's moderators is dropped — the
    /// queue and the inbox keep the case — and the drop is counted, because from outside it looks like no
    /// cases at all.
    /// </summary>
    [Fact]
    public async Task A_second_queue_mail_within_the_interval_is_suppressed_and_counted()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-interval");
        var posts = await _reports.PostsAsync(author, communityId, 2);
        using var suppressed = new MetricCollector<long>(factory.Services.GetRequiredService<IMeterFactory>(), "Songly.Notification.Mail", "songly.moderation.mail.suppressed");
        Mails.ClearFor(owner.User.Email);

        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modmail-interval-r1"), posts[0]);
        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modmail-interval-r2"), posts[1]);

        Assert.Single(Mails.SentTo(owner.User.Email));
        Assert.Equal(2, (await new NotificationTestClient(_client).OfTypeAsync(owner, "COMMUNITY_POST_REPORTED")).Count);
        Assert.Contains(suppressed.GetMeasurementSnapshot(), m => (string?)m.Tags["kind"] == "report");
    }

    /// <summary>The interval is per kind of case: a report silenced in the window does not silence an appeal in the same community.</summary>
    [Fact]
    public async Task The_interval_is_kept_per_kind_of_case()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-kinds");
        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "modmail-kinds-reporter"), await _reports.Content.PublishedInAsync(author, communityId));
        await _reports.Communities.BannedAsync(owner, communityId, author.Id);
        Mails.ClearFor(owner.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.AppealAsync(author, communityId, "to nieporozumienie")).StatusCode);

        var mail = Assert.Single(Mails.SentTo(owner.User.Email));
        Assert.Contains("odwołanie", mail.Subject, StringComparison.Ordinal);

        // The appeal's text is not in it (original ADR 0015): the mail says there is a reason to look.
        Assert.DoesNotContain("nieporozumienie", mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_reporter_gets_a_mail_when_their_report_is_dismissed()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("modmail-dis");
        var reporter = await factory.CreateLoggedInUserAsync(_client, "modmail-dis-reporter");
        await _reports.ReportedAsync(reporter, await _reports.Content.PublishedInAsync(author, communityId));
        Mails.ClearFor(reporter.User.Email);

        await _reports.DismissAllAsync(owner, communityId);

        var mail = Mails.LastTo(reporter.User.Email);
        Assert.Contains("rozpatrzone", mail.Subject, StringComparison.Ordinal);
        Assert.Contains(await CommunityNameAsync(owner, communityId), mail.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_appellant_is_mailed_the_answer_both_ways()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modmail-appeal-answer");
        await _reports.Communities.BannedAsync(owner, communityId, troll.Id);
        await _reports.Communities.AppealAsync(troll, communityId);
        Mails.ClearFor(troll.User.Email);
        await _reports.Communities.RejectAppealAsync(owner, communityId, troll.Id);
        Assert.Contains("odrzucone", Mails.LastTo(troll.User.Email).Subject, StringComparison.Ordinal);

        var (otherOwner, otherTroll, otherCommunityId) = await _reports.CommunityWithAuthorAsync("modmail-appeal-accepted");
        await _reports.Communities.BannedAsync(otherOwner, otherCommunityId, otherTroll.Id);
        await _reports.Communities.AppealAsync(otherTroll, otherCommunityId);
        Mails.ClearFor(otherTroll.User.Email);
        await _reports.Communities.UnbanAsync(otherOwner, otherCommunityId, otherTroll.Id);
        Assert.Contains("uznane", Mails.LastTo(otherTroll.User.Email).Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second instance (original ADR 0028): the escalation calls every administrator, and the overturn
    /// tells both the appellant and the moderators whose decision it undid.
    /// </summary>
    [Fact]
    public async Task The_second_instance_is_called_and_its_overturn_told_to_both_sides()
    {
        var admin = await factory.CreateAdminAsync(_client, "modmail-escalation-admin");
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("modmail-escalation");
        await _reports.Communities.BannedAsync(owner, communityId, troll.Id);
        await _reports.Communities.AppealAsync(troll, communityId, "prosze o ponowne rozpatrzenie");
        await _reports.Communities.RejectAppealAsync(owner, communityId, troll.Id);
        Mails.ClearFor(admin.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.EscalateAsync(troll, communityId)).StatusCode);

        var called = Mails.LastTo(admin.User.Email);
        Assert.Contains("administracji", called.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("ponowne", called.Body, StringComparison.Ordinal);

        Clear(owner, troll);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/communities/{communityId}/bans/{troll.Id}", admin.Token)).StatusCode);

        Assert.Contains("uchyliła", Mails.LastTo(troll.User.Email).Subject, StringComparison.Ordinal);
        Assert.Contains("uchyliła", Mails.LastTo(owner.User.Email).Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mail goes out for moderation decisions only. A like is in the inbox but not mailed: the inbox can hide
    /// a blocked user's notification when read, a mail cannot be taken back.
    /// </summary>
    [Fact]
    public async Task Ordinary_activity_sends_no_mail()
    {
        var author = await factory.CreateLoggedInUserAsync(_client, "modmail-like-author");
        var fan = await factory.CreateLoggedInUserAsync(_client, "modmail-like-fan");
        var postId = await _reports.Content.PublishAsync(author, "zwykly post");
        Mails.ClearFor(author.User.Email);

        await _reports.Content.LikeAsync(fan, postId);
        await _reports.Content.CommentAsync(fan, postId);

        Assert.Empty(Mails.SentTo(author.User.Email));
    }

    /// <summary>A change of the moderator role is no moderation decision (original ADR 0016) — no mail.</summary>
    [Fact]
    public async Task A_change_of_role_sends_no_mail()
    {
        var (owner, member, communityId) = await _reports.CommunityWithAuthorAsync("modmail-role");
        Mails.ClearFor(member.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.SetRoleAsync(owner, communityId, member.Id, "MODERATOR")).StatusCode);

        Assert.Empty(Mails.SentTo(member.User.Email));
    }

    /// <summary>
    /// A failed delivery is caught per recipient: one undeliverable address does not stop the mail to the
    /// other moderators, and the report itself succeeds.
    /// </summary>
    [Fact]
    public async Task A_delivery_failure_for_one_moderator_does_not_stop_the_others()
    {
        var mails = new FailingForOneMailSender();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IMailSender>(mails)));
        using var client = app.CreateClient();
        var reports = new ReportTestClient(factory, client);
        var (owner, author, communityId) = await reports.CommunityWithAuthorAsync("modmail-failure");
        var moderator = await factory.CreateLoggedInUserAsync(client, "modmail-failure-mod");
        await reports.Communities.JoinedAsync(moderator, communityId);
        await reports.Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR");
        var postId = await reports.Content.PublishedInAsync(author, communityId);
        mails.FailFor = owner.User.Email;

        await reports.ReportedAsync(await factory.CreateLoggedInUserAsync(client, "modmail-failure-reporter"), postId);

        Assert.Contains(moderator.User.Email, mails.Delivered);
        Assert.DoesNotContain(owner.User.Email, mails.Delivered);
    }

    private async Task<LoggedInUser> ModeratorAsync(LoggedInUser owner, Guid communityId, string prefix)
    {
        var moderator = await factory.CreateLoggedInUserAsync(_client, prefix);
        await _reports.Communities.JoinedAsync(moderator, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        return moderator;
    }

    private async Task<string> CommunityNameAsync(LoggedInUser viewer, Guid communityId)
    {
        using var response = await _client.GetAsync($"/api/v1/communities/{communityId}", viewer.Token);
        return (await Profile.ProfileTestExtensions.ReadJsonAsync(response)).GetProperty("name").GetString()!;
    }

    private void Clear(params LoggedInUser[] users)
    {
        foreach (var user in users)
        {
            Mails.ClearFor(user.User.Email);
        }
    }

    /// <summary>A mail server that refuses one address and takes the rest.</summary>
    private sealed class FailingForOneMailSender : IMailSender
    {
        public string? FailFor { get; set; }

        public ConcurrentBag<string> Delivered { get; } = [];

        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
        {
            if (email.To == FailFor)
            {
                throw new MailDeliveryException("The mail server refused this address");
            }

            Delivered.Add(email.To);
            return Task.CompletedTask;
        }
    }
}
