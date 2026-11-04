using System.Net;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// A notification is a reaction to a committed fact (this repository's ADR 0012): when it cannot be
/// written, the like it is about still stands and the request still succeeds.
/// </summary>
/// <remarks>
/// The inbox is broken on purpose — its table renamed away — so it runs on a
/// <see cref="TemporaryDatabase"/> of its own, never on the shared one.
/// </remarks>
public sealed class NotificationFailureTests(SonglyApiFactory factory)
{
    [Fact]
    public async Task A_like_stands_when_its_notification_cannot_be_written()
    {
        await using var database = await TemporaryDatabase.CreateAsync(factory);
        await using var app = factory.WithSettings(database.Settings);
        using var client = app.CreateClient();
        var author = await RegisterAsync(app, client, "notif-failure-author");
        var fan = await RegisterAsync(app, client, "notif-failure-fan");
        var content = new ContentTestClient(client);
        var postId = await content.PublishAsync(author);

        await database.ExecuteSqlAsync("ALTER TABLE notifications RENAME TO notifications_gone");

        Assert.Equal(HttpStatusCode.NoContent, (await content.LikeAsync(fan, postId)).StatusCode);
        Assert.Equal(1, (await content.PostAsync(author, postId)).GetProperty("likeCount").GetInt64());
    }

    private static async Task<LoggedInUser> RegisterAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> app, HttpClient client, string prefix)
    {
        var email = TestAccounts.UniqueEmail(prefix);
        var tokens = await client.RegisterAndVerifyAsync(app.MailsOf(), email);
        return new LoggedInUser(new TestUser(TestTokens.SubjectOf(tokens.AccessToken), email, TestAccounts.DefaultPassword), tokens);
    }
}
