using System.Net;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// The notification part of the original <c>AccountDeletionFlowIntegrationTest</c>: both columns of
/// <c>notifications</c> point at <c>users</c>, so the account's notifications go — those it received
/// and those it caused in other people's inboxes.
/// </summary>
public sealed class NotificationAccountDeletionTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());

    [Fact]
    public async Task Deleting_an_account_removes_its_notifications_both_ways()
    {
        var leaving = await factory.CreateLoggedInUserAsync(_client, "notif-leaving");
        var staying = await factory.CreateLoggedInUserAsync(_client, "notif-staying");
        await _content.LikeAsync(staying, await _content.PublishAsync(leaving));
        await _content.LikeAsync(leaving, await _content.PublishAsync(staying));

        Assert.Equal(HttpStatusCode.NoContent, (await new CommunityTestClient(factory, _client).DeleteAccountAsync(leaving)).StatusCode);

        Assert.Empty(await new NotificationTestClient(_client).ListAsync(staying));
        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM notifications WHERE user_id = @id OR actor_id = @id", ("id", leaving.Id)));
    }
}
