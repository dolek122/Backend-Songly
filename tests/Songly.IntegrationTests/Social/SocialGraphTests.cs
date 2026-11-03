using Microsoft.Extensions.DependencyInjection;
using Songly.Social.Contracts;

namespace Songly.IntegrationTests.Social;

/// <summary>The social module's boundary, used the way the feed and the content module will use it.</summary>
public sealed class SocialGraphTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Friends_are_the_other_end_of_each_friendship()
    {
        var me = await factory.CreateLoggedInUserAsync(_client, "graph-me");
        var first = await factory.CreateLoggedInUserAsync(_client, "graph-first");
        var second = await factory.CreateLoggedInUserAsync(_client, "graph-second");
        await BecomeFriends(me, first);
        await BecomeFriends(second, me);

        Assert.Equal(new HashSet<Guid> { first.Id, second.Id }, await Graph(g => g.FriendIdsAsync(me.Id, TestContext.Current.CancellationToken)));
    }

    /// <summary>Both directions, combined — content hides both whom I block and who blocks me.</summary>
    [Fact]
    public async Task Blocked_either_way_combines_both_directions()
    {
        var me = await factory.CreateLoggedInUserAsync(_client, "graph-blocks");
        var blockedByMe = await factory.CreateLoggedInUserAsync(_client, "graph-blocked-by-me");
        var blockingMe = await factory.CreateLoggedInUserAsync(_client, "graph-blocking-me");
        var stranger = await factory.CreateLoggedInUserAsync(_client, "graph-stranger");
        await _client.PutJsonAsync($"/api/v1/blocks/{blockedByMe.Id}", new { }, me.Token);
        await _client.PutJsonAsync($"/api/v1/blocks/{me.Id}", new { }, blockingMe.Token);

        Assert.Equal(new HashSet<Guid> { blockedByMe.Id, blockingMe.Id }, await Graph(g => g.BlockedEitherWayIdsAsync(me.Id, TestContext.Current.CancellationToken)));
        Assert.True(await Graph(g => g.IsBlockedEitherWayAsync(me.Id, blockingMe.Id, TestContext.Current.CancellationToken)));
        Assert.True(await Graph(g => g.IsBlockedEitherWayAsync(blockedByMe.Id, me.Id, TestContext.Current.CancellationToken)));
        Assert.False(await Graph(g => g.IsBlockedEitherWayAsync(me.Id, stranger.Id, TestContext.Current.CancellationToken)));
    }

    private async Task BecomeFriends(LoggedInUser from, LoggedInUser to)
    {
        var sent = await _client.PostJsonAsync("/api/v1/friends/requests", new { addresseeId = to.Id }, from.Token);
        var requestId = (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<System.Text.Json.JsonElement>(sent.Content, TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        await _client.PostJsonAsync($"/api/v1/friends/requests/{requestId}/accept", new { }, to.Token);
    }

    private async Task<T> Graph<T>(Func<ISocialGraph, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ISocialGraph>());
    }
}
