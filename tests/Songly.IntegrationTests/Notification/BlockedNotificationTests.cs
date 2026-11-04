using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Content;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// The block, closed on the inbox side. Ported from the original
/// <c>BlockedNotificationIntegrationTest</c>: the content module stops <b>new</b> approaches after a
/// block, but the ones from before stayed in the inbox — with a name, an avatar, counted in the badge.
/// </summary>
/// <remarks>
/// Filtered when read, not deleted when blocking — so the tests also check that <b>unblocking brings
/// the inbox back whole</b>, which deleted rows could not.
/// </remarks>
public sealed class BlockedNotificationTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());
    private readonly NotificationTestClient _inbox = new(factory.CreateClient());

    [Fact]
    public async Task Notifications_from_a_blocked_user_are_hidden_and_restored_on_unblock()
    {
        var author = await UserAsync("notif-block-author");
        var troll = await UserAsync("notif-block-troll");
        await LikeAsync(author, troll);
        Assert.Equal(troll.Id, Assert.Single(await _inbox.ListAsync(author)).GetProperty("actorId").GetGuid());

        await _content.BlockAsync(author, troll);

        Assert.Empty(await _inbox.ListAsync(author));
        Assert.Equal(0, (await _inbox.PageAsync(author)).GetProperty("page").GetProperty("totalElements").GetInt64());
        Assert.Equal(0, await _inbox.UnreadCountAsync(author));

        await UnblockAsync(author, troll);

        Assert.Equal(troll.Id, Assert.Single(await _inbox.ListAsync(author)).GetProperty("actorId").GetGuid());
        Assert.Equal(1, await _inbox.UnreadCountAsync(author));
    }

    /// <summary>
    /// A hidden notification stays unread. Were "read all" to reach it, it would come back read after
    /// the unblock — the block quietly changing the state of something the user never saw.
    /// </summary>
    [Fact]
    public async Task Read_all_does_not_consume_hidden_notifications()
    {
        var author = await UserAsync("notif-block-markall");
        var troll = await UserAsync("notif-block-markall-troll");
        await LikeAsync(author, troll);

        await _content.BlockAsync(author, troll);
        await _inbox.ReadAllAsync(author);
        await UnblockAsync(author, troll);

        Assert.Equal(1, await _inbox.UnreadCountAsync(author));
        Assert.Equal(JsonValueKind.Null, Assert.Single(await _inbox.ListAsync(author)).GetProperty("readAt").ValueKind);
    }

    /// <summary>The block works both ways here too: the blocked user stops seeing notifications from the one who blocked them.</summary>
    [Fact]
    public async Task The_block_is_symmetric_in_the_inbox_too()
    {
        var blocker = await UserAsync("notif-block-sym-blocker");
        var other = await UserAsync("notif-block-sym-other");
        await LikeAsync(other, blocker);

        await _content.BlockAsync(blocker, other);

        Assert.Empty(await _inbox.ListAsync(other));
    }

    /// <summary>The filter is per actor: blocking one person leaves everyone else's notifications in place.</summary>
    [Fact]
    public async Task Only_the_blocked_actor_is_hidden()
    {
        var author = await UserAsync("notif-block-only-author");
        var troll = await UserAsync("notif-block-only-troll");
        var friend = await UserAsync("notif-block-only-friend");
        await LikeAsync(author, troll);
        await LikeAsync(author, friend);

        await _content.BlockAsync(author, troll);

        Assert.Equal(friend.Id, Assert.Single(await _inbox.ListAsync(author)).GetProperty("actorId").GetGuid());
        Assert.Equal(1, await _inbox.UnreadCountAsync(author));
    }

    private Task<LoggedInUser> UserAsync(string prefix) => factory.CreateLoggedInUserAsync(_client, prefix);

    /// <summary>The notification is made before either side blocks — the only moment it can be, since the interaction itself is cut off afterwards.</summary>
    private async Task LikeAsync(LoggedInUser author, LoggedInUser liker) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(liker, await _content.PublishAsync(author, "post autora"))).StatusCode);

    private async Task UnblockAsync(LoggedInUser blocker, LoggedInUser blocked) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/blocks/{blocked.Id}", blocker.Token)).StatusCode);
}
