using System.Net;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// Suspensions and their appeals by e-mail (original ADR 0022, 0023). Ported from the mail parts of the
/// original <c>AccountSuspensionIntegrationTest</c> and <c>SuspensionAppealIntegrationTest</c>. For a
/// suspended account mail is the only channel: it has no access to the inbox.
/// </summary>
public sealed class SuspensionMailTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private RecordingMailSender Mails => factory.Mails;

    /// <summary>
    /// The mail carries the term but <b>never the reason</b> — an administrator's text does not travel a push
    /// channel (original ADR 0015). Re-deciding an ongoing suspension sends no second mail about the same
    /// thing, and lifting it says the account is back.
    /// </summary>
    [Fact]
    public async Task The_mail_carries_the_term_but_never_the_reason()
    {
        var admin = await factory.CreateAdminAsync(_client, "suspmail-admin");
        var troll = await factory.CreateLoggedInUserAsync(_client, "suspmail-troll");
        Mails.ClearFor(troll.User.Email);

        await SuspendAsync(admin, troll, durationDays: 7);

        var mail = Mails.LastTo(troll.User.Email);
        Assert.Contains("zawieszone", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("wygasa za 7 dni", mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("nękanie innych", mail.Body, StringComparison.Ordinal);

        await SuspendAsync(admin, troll, durationDays: 14);
        Assert.Single(Mails.SentTo(troll.User.Email));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{troll.Id}/suspension", admin.Token)).StatusCode);
        Assert.Contains("zdjęte", Mails.LastTo(troll.User.Email).Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_suspension_without_a_term_says_so()
    {
        var admin = await factory.CreateAdminAsync(_client, "suspmail-forever-admin");
        var troll = await factory.CreateLoggedInUserAsync(_client, "suspmail-forever");
        Mails.ClearFor(troll.User.Email);

        await SuspendAsync(admin, troll, durationDays: null);

        Assert.Contains("Zawieszenie jest bezterminowe.", Mails.LastTo(troll.User.Email).Body, StringComparison.Ordinal);
    }

    /// <summary>A suspension that already expired is only cleaned up when lifted: the account worked, there is nothing to tell.</summary>
    [Fact]
    public async Task Lifting_an_expired_suspension_sends_nothing()
    {
        var admin = await factory.CreateAdminAsync(_client, "suspmail-expired-admin");
        var troll = await factory.CreateLoggedInUserAsync(_client, "suspmail-expired");
        await factory.SuspendAsync(troll.User, DateTimeOffset.UtcNow.AddDays(-1));
        Mails.ClearFor(troll.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{troll.Id}/suspension", admin.Token)).StatusCode);

        Assert.Empty(Mails.SentTo(troll.User.Email));
    }

    /// <summary>
    /// The appeal reaches every administrator by mail, without its text — the text waits in the list of
    /// suspensions, the pull channel. A second appeal is a 409, not a second mail.
    /// </summary>
    [Fact]
    public async Task An_appeal_mails_the_administrators_without_its_text_and_only_once()
    {
        var admin = await factory.CreateAdminAsync(_client, "suspmail-appeal-admin");
        var troll = await factory.CreateLoggedInUserAsync(_client, "suspmail-appeal");
        await SuspendAsync(admin, troll, durationDays: null);
        Mails.ClearFor(admin.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await AppealAsync(troll, "to byla pomylka, nie ja pisalem")).StatusCode);

        var mail = Assert.Single(Mails.SentTo(admin.User.Email));
        Assert.Contains("odwołanie", mail.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("pomylka", mail.Body, StringComparison.Ordinal);

        await (await AppealAsync(troll, "drugie")).ReadErrorAsync(HttpStatusCode.Conflict, "SUSPENSION_APPEAL_ALREADY_SUBMITTED");
        Assert.Single(Mails.SentTo(admin.User.Email));
    }

    /// <summary>
    /// Lifting a suspension with a pending appeal accepts it — and sends <b>one</b> mail saying why the account
    /// is back, not "accepted" and "lifted" about the same thing.
    /// </summary>
    [Fact]
    public async Task Lifting_with_a_pending_appeal_accepts_it_with_one_mail()
    {
        var admin = await factory.CreateAdminAsync(_client, "suspmail-accept-admin");
        var troll = await factory.CreateLoggedInUserAsync(_client, "suspmail-accept");
        await SuspendAsync(admin, troll, durationDays: null);
        await AppealAsync(troll, "obiecuje poprawe");
        Mails.ClearFor(troll.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{troll.Id}/suspension", admin.Token)).StatusCode);

        Assert.Contains("uznane", Assert.Single(Mails.SentTo(troll.User.Email)).Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_appeal_is_mailed_to_the_appellant()
    {
        var admin = await factory.CreateAdminAsync(_client, "suspmail-reject-admin");
        var troll = await factory.CreateLoggedInUserAsync(_client, "suspmail-reject");
        await SuspendAsync(admin, troll, durationDays: null);
        await AppealAsync(troll, "nie zgadzam sie");
        Mails.ClearFor(troll.User.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync($"/api/v1/admin/users/{troll.Id}/suspension/appeal/rejection", new { }, admin.Token)).StatusCode);

        Assert.Contains("odrzucone", Assert.Single(Mails.SentTo(troll.User.Email)).Subject, StringComparison.Ordinal);
    }

    /// <summary>A suspension is a decision about me, so the switch for those turns its mail off too — and only the mail.</summary>
    [Fact]
    public async Task The_mail_preferences_apply_to_suspensions_too()
    {
        var admin = await factory.CreateAdminAsync(_client, "suspmail-prefs-admin");
        var troll = await factory.CreateLoggedInUserAsync(_client, "suspmail-prefs");
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/notifications/mail-preferences", new { decisionsAboutMe = false, moderationQueue = true }, troll.Token)).StatusCode);
        Mails.ClearFor(troll.User.Email);

        await SuspendAsync(admin, troll, durationDays: 3);

        Assert.Empty(Mails.SentTo(troll.User.Email));
    }

    private async Task SuspendAsync(LoggedInUser admin, LoggedInUser target, int? durationDays) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{target.Id}/suspension", new { reason = "nękanie innych", durationDays }, admin.Token)).StatusCode);

    private Task<HttpResponseMessage> AppealAsync(LoggedInUser user, string text) =>
        _client.PostJsonAsync("/api/v1/auth/me/suspension/appeal", new { text }, user.Token);
}
