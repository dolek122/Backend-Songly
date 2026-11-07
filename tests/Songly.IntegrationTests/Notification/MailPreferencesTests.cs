using System.Net;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// The e-mail preferences (original ADR 0020). Ported from the original <c>MailPreferencesIntegrationTest</c>.
/// The invariant matters more than the switch: turning mail off <b>leaves the inbox alone</b> — a
/// moderation decision cannot be declined, and an appeal means nothing if one need not learn of the ban.
/// </summary>
public sealed class MailPreferencesTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly NotificationTestClient _inbox = new(factory.CreateClient());
    private readonly ReportTestClient _reports = new(factory, factory.CreateClient());

    /// <summary>No row is consent — these mails follow from using the service, they are not marketing.</summary>
    [Fact]
    public async Task Both_are_on_by_default_and_an_update_persists()
    {
        var someone = await factory.CreateLoggedInUserAsync(_client, "mailprefs-default");
        var defaults = await PreferencesAsync(someone);
        Assert.True(defaults.GetProperty("decisionsAboutMe").GetBoolean());
        Assert.True(defaults.GetProperty("moderationQueue").GetBoolean());

        await SetAsync(someone, decisionsAboutMe: false, moderationQueue: true);

        var updated = await PreferencesAsync(someone);
        Assert.False(updated.GetProperty("decisionsAboutMe").GetBoolean());
        Assert.True(updated.GetProperty("moderationQueue").GetBoolean());

        await SetAsync(someone, decisionsAboutMe: true, moderationQueue: false);
        Assert.False((await PreferencesAsync(someone)).GetProperty("moderationQueue").GetBoolean());
    }

    /// <summary><c>PUT</c> replaces the settings whole, so a missing field is a validation error, not "leave it".</summary>
    [Fact]
    public async Task Both_fields_are_required()
    {
        var someone = await factory.CreateLoggedInUserAsync(_client, "mailprefs-required");

        await (await _client.PutJsonAsync("/api/v1/notifications/mail-preferences", new { decisionsAboutMe = false }, someone.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>The point of ADR 0020: opting out of mail silences <b>only</b> mail. The ban stays in the inbox — the record of a decision one can appeal.</summary>
    [Fact]
    public async Task Turning_decision_mail_off_leaves_the_inbox_untouched()
    {
        var (owner, troll, communityId) = await _reports.CommunityWithAuthorAsync("mailprefs-ban");
        await SetAsync(troll, decisionsAboutMe: false, moderationQueue: true);
        factory.Mails.ClearFor(troll.User.Email);

        await _reports.Communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Empty(factory.Mails.SentTo(troll.User.Email));
        Assert.Single(await _inbox.OfTypeAsync(troll, "COMMUNITY_BANNED"));
    }

    /// <summary>Filtered <b>per recipient</b>: one moderator opting out of the queue must not silence the mail for the others — a report goes to all at once.</summary>
    [Fact]
    public async Task Queue_mail_is_filtered_per_recipient()
    {
        var (owner, author, communityId) = await _reports.CommunityWithAuthorAsync("mailprefs-queue");
        var moderator = await ModeratorAsync(owner, communityId, "mailprefs-queue-mod");
        var postId = await _reports.Content.PublishedInAsync(author, communityId, "sporny post");
        await SetAsync(owner, decisionsAboutMe: true, moderationQueue: false);
        factory.Mails.ClearFor(owner.User.Email);
        factory.Mails.ClearFor(moderator.User.Email);

        await _reports.ReportedAsync(await factory.CreateLoggedInUserAsync(_client, "mailprefs-queue-reporter"), postId);

        Assert.Empty(factory.Mails.SentTo(owner.User.Email));
        Assert.Single(factory.Mails.SentTo(moderator.User.Email));
        Assert.Single(await _inbox.OfTypeAsync(owner, "COMMUNITY_POST_REPORTED"));
    }

    /// <summary>The two switches are independent, because they match two roles: a moderator flooded by their community's mail must not lose word of their own account.</summary>
    [Fact]
    public async Task The_switches_are_independent()
    {
        var (owner, _, communityId) = await _reports.CommunityWithAuthorAsync("mailprefs-indep");
        var moderator = await ModeratorAsync(owner, communityId, "mailprefs-indep-mod");
        await SetAsync(moderator, decisionsAboutMe: true, moderationQueue: false);
        factory.Mails.ClearFor(moderator.User.Email);

        await _reports.Communities.BannedAsync(owner, communityId, moderator.Id);

        Assert.Contains("ban", Assert.Single(factory.Mails.SentTo(moderator.User.Email)).Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_preferences_require_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/notifications/mail-preferences", accessToken: null)).StatusCode);
    }

    private async Task<LoggedInUser> ModeratorAsync(LoggedInUser owner, Guid communityId, string prefix)
    {
        var moderator = await factory.CreateLoggedInUserAsync(_client, prefix);
        await _reports.Communities.JoinedAsync(moderator, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await _reports.Communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        return moderator;
    }

    private async Task<System.Text.Json.JsonElement> PreferencesAsync(LoggedInUser user)
    {
        using var response = await _client.GetAsync("/api/v1/notifications/mail-preferences", user.Token);
        return await response.ReadJsonAsync();
    }

    private async Task SetAsync(LoggedInUser user, bool decisionsAboutMe, bool moderationQueue) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/notifications/mail-preferences", new { decisionsAboutMe, moderationQueue }, user.Token)).StatusCode);
}
