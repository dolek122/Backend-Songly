using System.Net;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// Notifications of a change of the moderator role (original ADR 0016). Ported from the original
/// <c>ModeratorRoleNotificationIntegrationTest</c>: losing the role used to show only as 403s on the
/// report queue — a decision looking like a failure.
/// </summary>
public sealed class ModeratorRoleNotificationTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly NotificationTestClient _inbox = new(factory.CreateClient());

    [Fact]
    public async Task Granting_and_revoking_notify_the_member()
    {
        var (owner, member, communityId, communities) = await CommunityWithMemberAsync("rolenotif-flow");

        await SetRoleAsync(communities, owner, communityId, member, "MODERATOR");
        var granted = Assert.Single(await _inbox.OfTypeAsync(member, "COMMUNITY_MODERATOR_GRANTED"));
        Assert.Equal(owner.Id, granted.GetProperty("actorId").GetGuid());
        Assert.Equal(communityId, granted.GetProperty("resourceId").GetGuid());

        await SetRoleAsync(communities, owner, communityId, member, "MEMBER");
        Assert.Single(await _inbox.OfTypeAsync(member, "COMMUNITY_MODERATOR_REVOKED"));
    }

    /// <summary>
    /// Setting the role a member already has is no change — the same rule as "only a new ban" (original
    /// ADR 0014): the owner's repeated click adds no notifications about the same thing.
    /// </summary>
    [Fact]
    public async Task Setting_the_same_role_again_does_not_notify()
    {
        var (owner, member, communityId, communities) = await CommunityWithMemberAsync("rolenotif-idem");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await SetRoleAsync(communities, owner, communityId, member, "MODERATOR");
        }

        await SetRoleAsync(communities, owner, communityId, member, "MEMBER");
        await SetRoleAsync(communities, owner, communityId, member, "MEMBER");

        Assert.Single(await _inbox.OfTypeAsync(member, "COMMUNITY_MODERATOR_GRANTED"));
        Assert.Single(await _inbox.OfTypeAsync(member, "COMMUNITY_MODERATOR_REVOKED"));
    }

    /// <summary>
    /// A change of role is an ordinary notification, not a moderation one (original ADR 0016): it grants
    /// or takes away rights but restricts nothing in ordinary participation. So the block hides it —
    /// whoever blocked the owner does not see this from them either — and the name stays: the owner
    /// acts as the owner, not as the community's anonymous moderation.
    /// </summary>
    [Fact]
    public async Task A_change_of_role_is_hidden_by_a_block_and_signed_by_the_owner()
    {
        var (owner, member, communityId, communities) = await CommunityWithMemberAsync("rolenotif-block");

        await SetRoleAsync(communities, owner, communityId, member, "MODERATOR");
        Assert.NotNull(Assert.Single(await _inbox.OfTypeAsync(member, "COMMUNITY_MODERATOR_GRANTED")).GetProperty("actorDisplayName").GetString());

        await new ContentTestClient(_client).BlockAsync(member, owner);

        Assert.Empty(await _inbox.OfTypeAsync(member, "COMMUNITY_MODERATOR_GRANTED"));
    }

    private async Task<(LoggedInUser Owner, LoggedInUser Member, Guid CommunityId, CommunityTestClient Communities)> CommunityWithMemberAsync(string prefix)
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-member");
        var communities = new CommunityTestClient(factory, _client);
        var communityId = await communities.CreateAsync(owner, prefix);
        await communities.JoinedAsync(member, communityId);
        return (owner, member, communityId, communities);
    }

    private static async Task SetRoleAsync(CommunityTestClient communities, LoggedInUser owner, Guid communityId, LoggedInUser target, string role) =>
        Assert.Equal(HttpStatusCode.NoContent, (await communities.SetRoleAsync(owner, communityId, target.Id, role)).StatusCode);
}
