using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Songly.Community.Contracts;

namespace Songly.IntegrationTests.Community;

/// <summary>
/// Bans, permanent and temporary. Ported from the ban scenarios of the original
/// <c>CommunityModerationIntegrationTest</c>, <c>TemporaryBanIntegrationTest</c> and
/// <c>MembershipStatusIntegrationTest</c>.
/// </summary>
public sealed class BanTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly CommunityTestClient _communities = new(factory, factory.CreateClient());

    /// <summary>
    /// The point of a ban: removing someone from the member list alone would do nothing, because
    /// joining is open and immediate. Only blocking the return makes the decision last.
    /// </summary>
    [Fact]
    public async Task A_banned_user_loses_the_membership_and_cannot_join_again()
    {
        var (owner, troll, communityId) = await Scenario("ban-main");

        await _communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal("BANNED", (await _communities.StatusAsync(troll, communityId)).GetProperty("status").GetString());
        await (await _communities.JoinAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ICommunityMembership>().IsBannedAsync(communityId, troll.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lifting_the_ban_restores_the_right_to_join()
    {
        var (owner, troll, communityId) = await Scenario("ban-lift");
        await _communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _communities.UnbanAsync(owner, communityId, troll.Id)).StatusCode);

        await _communities.JoinedAsync(troll, communityId);
    }

    /// <summary>
    /// The owner cannot be banned, and a moderator cannot ban a moderator — otherwise two moderators
    /// could throw each other out and whoever clicked first would win. A member cannot ban at all.
    /// </summary>
    [Fact]
    public async Task Who_may_ban_whom()
    {
        var (owner, member, communityId) = await Scenario("ban-who");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "ban-who-moderator");
        var other = await factory.CreateLoggedInUserAsync(_client, "ban-who-other");
        await _communities.JoinedAsync(moderator, communityId);
        await _communities.JoinedAsync(other, communityId);
        await _communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR");
        await _communities.SetRoleAsync(owner, communityId, other.Id, "MODERATOR");

        await (await _communities.BanAsync(moderator, communityId, owner.Id)).ReadErrorAsync(HttpStatusCode.Forbidden, "CANNOT_MODERATE_OWNER");
        await (await _communities.BanAsync(owner, communityId, owner.Id)).ReadErrorAsync(HttpStatusCode.Forbidden, "CANNOT_MODERATE_OWNER");
        await (await _communities.BanAsync(moderator, communityId, other.Id)).ReadErrorAsync(HttpStatusCode.Forbidden, "CANNOT_MODERATE_MODERATOR");
        await (await _communities.BanAsync(member, communityId, moderator.Id)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MODERATOR");

        // A moderator bans a member, the owner bans a moderator.
        await _communities.BannedAsync(moderator, communityId, member.Id);
        await _communities.BannedAsync(owner, communityId, other.Id);
    }

    /// <summary>A ban on an account that does not exist trips a foreign key — a conflict, not a server error.</summary>
    [Fact]
    public async Task Banning_a_nonexistent_user_is_a_conflict()
    {
        var (owner, _, communityId) = await Scenario("ban-nobody");

        await (await _communities.BanAsync(owner, communityId, Guid.NewGuid(), "spam")).ReadErrorAsync(HttpStatusCode.Conflict, "CONFLICT");
    }

    /// <summary>The banned user sees the reason and the term (original ADR 0015) — never who imposed it.</summary>
    [Fact]
    public async Task The_banned_user_sees_the_reason_and_the_term()
    {
        var (owner, troll, communityId) = await Scenario("ban-status");
        await _communities.BannedAsync(owner, communityId, troll.Id, "spam w kazdym watku", 7);

        var status = await _communities.StatusAsync(troll, communityId);

        Assert.Equal("BANNED", status.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("role").ValueKind);
        var ban = status.GetProperty("ban");
        Assert.Equal("spam w kazdym watku", ban.GetProperty("reason").GetString());
        Assert.NotEqual(JsonValueKind.Null, ban.GetProperty("createdAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, ban.GetProperty("expiresAt").ValueKind);
        Assert.False(ban.TryGetProperty("bannedBy", out _));
    }

    /// <summary>A ban needs no body: without a reason and a term it is permanent, and reads as such — not as a 500.</summary>
    [Fact]
    public async Task A_ban_without_a_body_is_permanent_and_has_no_reason()
    {
        var (owner, troll, communityId) = await Scenario("ban-nobody-body");

        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, $"/api/v1/communities/{communityId}/bans/{troll.Id}") { Headers = { Authorization = new("Bearer", owner.Token) } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var ban = (await _communities.StatusAsync(troll, communityId)).GetProperty("ban");
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task A_temporary_ban_blocks_the_return_and_carries_its_term_to_the_ban_list()
    {
        var (owner, troll, communityId) = await Scenario("ban-temporary");

        await _communities.BannedAsync(owner, communityId, troll.Id, "przerwa", 3);

        await (await _communities.JoinAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");
        var listed = Assert.Single((await _communities.BansAsync(owner, communityId)).EnumerateArray());
        var expiresAt = listed.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow.AddDays(3).AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(3).AddMinutes(5));
        Assert.Equal(owner.Id, listed.GetProperty("bannedBy").GetGuid());
    }

    /// <summary>
    /// Expiry is lazy: nothing happens when a ban runs out, everyone simply asks about an active ban.
    /// An expired ban reads as NONE, is gone from the list, and lets the user back in.
    /// </summary>
    [Fact]
    public async Task An_expired_ban_lets_the_user_back_in()
    {
        var (owner, troll, communityId) = await Scenario("ban-expired");
        await _communities.BannedAsync(owner, communityId, troll.Id, "przerwa", 1);
        await _communities.ExpireBanAsync(communityId, troll.Id);

        Assert.Equal("NONE", (await _communities.StatusAsync(troll, communityId)).GetProperty("status").GetString());
        Assert.Empty((await _communities.BansAsync(owner, communityId)).EnumerateArray());
        await _communities.JoinedAsync(troll, communityId);

        // Back in, the status describes the membership, though the expired row is still in the table.
        Assert.Equal("MEMBER", (await _communities.StatusAsync(troll, communityId)).GetProperty("status").GetString());
    }

    /// <summary>A ban after an expired one is a new decision: it overwrites the row, with its own reason and term.</summary>
    [Fact]
    public async Task A_ban_after_expiry_is_a_new_ban()
    {
        var (owner, troll, communityId) = await Scenario("ban-again");
        await _communities.BannedAsync(owner, communityId, troll.Id, "pierwszy", 1);
        await _communities.ExpireBanAsync(communityId, troll.Id);

        await _communities.BannedAsync(owner, communityId, troll.Id, "drugi", null);

        var ban = (await _communities.StatusAsync(troll, communityId)).GetProperty("ban");
        Assert.Equal("drugi", ban.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("expiresAt").ValueKind);
    }

    /// <summary>An active ban is untouchable: changing its term is unban + ban, not a quiet swap on a duplicated click.</summary>
    [Fact]
    public async Task Banning_again_while_active_changes_nothing()
    {
        var (owner, troll, communityId) = await Scenario("ban-active-again");
        await _communities.BannedAsync(owner, communityId, troll.Id, "pierwszy", 3);

        await _communities.BannedAsync(owner, communityId, troll.Id, "drugi", null);

        var ban = (await _communities.StatusAsync(troll, communityId)).GetProperty("ban");
        Assert.Equal("pierwszy", ban.GetProperty("reason").GetString());
        Assert.NotEqual(JsonValueKind.Null, ban.GetProperty("expiresAt").ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(36_501)]
    public async Task An_out_of_range_term_is_rejected(int durationDays)
    {
        var (owner, troll, communityId) = await Scenario("ban-term");

        await (await _communities.BanAsync(owner, communityId, troll.Id, "spam", durationDays)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Who is banned and why is information about a person — the only list in the module that is not public.</summary>
    [Fact]
    public async Task The_ban_list_is_for_moderators_only()
    {
        var (owner, troll, communityId) = await Scenario("ban-list");
        var member = await factory.CreateLoggedInUserAsync(_client, "ban-list-member");
        await _communities.JoinedAsync(member, communityId);
        await _communities.BannedAsync(owner, communityId, troll.Id);

        await (await _client.GetAsync($"/api/v1/communities/{communityId}/bans", member.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_MODERATOR");
        Assert.Equal(troll.Id, Assert.Single((await _communities.BansAsync(owner, communityId)).EnumerateArray()).GetProperty("userId").GetGuid());
    }

    /// <summary>
    /// The ban belongs to the community, not to who imposed it: a moderator deleting their account
    /// must not quietly let in everyone they banned. A deleted banned account takes its ban along.
    /// </summary>
    [Fact]
    public async Task Bans_outlive_the_moderator_who_imposed_them()
    {
        var (owner, troll, communityId) = await Scenario("ban-outlive");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "ban-outlive-moderator");
        var leavingTroll = await factory.CreateLoggedInUserAsync(_client, "ban-outlive-leaving");
        await _communities.JoinedAsync(moderator, communityId);
        await _communities.JoinedAsync(leavingTroll, communityId);
        await _communities.SetRoleAsync(owner, communityId, moderator.Id, "MODERATOR");
        await _communities.BannedAsync(moderator, communityId, troll.Id);
        await _communities.BannedAsync(moderator, communityId, leavingTroll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _communities.DeleteAccountAsync(moderator)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _communities.DeleteAccountAsync(leavingTroll)).StatusCode);

        var ban = Assert.Single((await _communities.BansAsync(owner, communityId)).EnumerateArray());
        Assert.Equal(troll.Id, ban.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("bannedBy").ValueKind);
        await (await _communities.JoinAsync(troll, communityId)).ReadErrorAsync(HttpStatusCode.Forbidden, "BANNED_FROM_COMMUNITY");
    }

    /// <summary>Deleting the account of an owner takes their community, bans included.</summary>
    [Fact]
    public async Task Deleting_the_owner_takes_the_community_and_its_bans()
    {
        var (owner, troll, communityId) = await Scenario("ban-owner-gone");
        await _communities.BannedAsync(owner, communityId, troll.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _communities.DeleteAccountAsync(owner)).StatusCode);

        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM community_bans WHERE community_id = @id", ("id", communityId)));
    }

    private async Task<(LoggedInUser Owner, LoggedInUser Member, Guid CommunityId)> Scenario(string prefix)
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, $"{prefix}-member");
        var communityId = await _communities.CreateAsync(owner, prefix);
        await _communities.JoinedAsync(member, communityId);
        return (owner, member, communityId);
    }
}
