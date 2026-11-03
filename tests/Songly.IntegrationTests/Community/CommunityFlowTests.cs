using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Songly.Community.Contracts;
using Songly.IntegrationTests.Profile;

namespace Songly.IntegrationTests.Community;

/// <summary>
/// Communities, membership and roles. Ported from the original <c>CommunityFlowIntegrationTest</c>,
/// <c>CommunityServiceTest</c>, the role scenarios of <c>CommunityModerationIntegrationTest</c> and the
/// non-ban scenarios of <c>MembershipStatusIntegrationTest</c>.
/// </summary>
public sealed class CommunityFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_join_list_leave_and_delete()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "community-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, "community-member");
        var created = await _client.PostJsonAsync("/api/v1/communities", new { name = Unique("Fani Metalu"), description = "o metalu" }, owner.Token);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var communityId = (await Json(created)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await Join(member, communityId)).StatusCode);

        var community = await Get(member, $"/api/v1/communities/{communityId}");
        Assert.Equal(2, community.GetProperty("memberCount").GetInt64());
        Assert.Equal("MEMBER", community.GetProperty("myRole").GetString());
        Assert.Equal(owner.User.Email.Split('@')[0], community.GetProperty("ownerDisplayName").GetString());

        var members = (await Get(owner, $"/api/v1/communities/{communityId}/members")).GetProperty("content");
        Assert.Equal(2, members.GetArrayLength());
        Assert.Equal("OWNER", members[0].GetProperty("role").GetString());
        Assert.Equal(owner.User.Email.Split('@')[0], members[0].GetProperty("displayName").GetString());

        Assert.Contains(communityId, (await Get(member, "/api/v1/communities/mine")).GetProperty("content").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}/membership", member.Token)).StatusCode);
        community = await Get(member, $"/api/v1/communities/{communityId}");
        Assert.Equal(1, community.GetProperty("memberCount").GetInt64());
        Assert.Equal(JsonValueKind.Null, community.GetProperty("myRole").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}", owner.Token)).StatusCode);
        await (await _client.GetAsync($"/api/v1/communities/{communityId}", owner.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
    }

    [Fact]
    public async Task Management_is_for_the_owner_and_names_are_unique_whatever_the_letter_case()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "community-rules-owner");
        var other = await factory.CreateLoggedInUserAsync(_client, "community-rules-other");
        var name = Unique("Jazzowa Piwnica");
        var communityId = await Create(owner, name);

        await (await _client.PostJsonAsync("/api/v1/communities", new { name = name.ToLowerInvariant() }, other.Token)).ReadErrorAsync(HttpStatusCode.Conflict, "COMMUNITY_NAME_TAKEN");
        await (await _client.PutJsonAsync($"/api/v1/communities/{communityId}", new { name = "Przejęta" }, other.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_OWNER");

        // Keeping one's own name, in another letter case too, is not a collision.
        var updated = await Json(await _client.PutJsonAsync($"/api/v1/communities/{communityId}", new { name = name.ToUpperInvariant(), description = "nowy opis" }, owner.Token));
        Assert.Equal("nowy opis", updated.GetProperty("description").GetString());

        await (await _client.DeleteAsync($"/api/v1/communities/{communityId}/membership", owner.Token)).ReadErrorAsync(HttpStatusCode.Conflict, "OWNER_CANNOT_LEAVE");
        await (await _client.DeleteAsync($"/api/v1/communities/{communityId}", other.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_OWNER");
    }

    [Fact]
    public async Task Renaming_to_another_communitys_name_is_refused()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "community-rename");
        var taken = Unique("Zajeta");
        await Create(owner, taken);
        var communityId = await Create(owner, Unique("Wolna"));

        await (await _client.PutJsonAsync($"/api/v1/communities/{communityId}", new { name = taken.ToUpperInvariant() }, owner.Token)).ReadErrorAsync(HttpStatusCode.Conflict, "COMMUNITY_NAME_TAKEN");
    }

    public static TheoryData<string?, string?> InvalidCommunities => new()
    {
        { "   ", null },
        { null, null },
        { new string('x', 101), null },
        { "Ok", new string('x', 501) },
    };

    [Theory]
    [MemberData(nameof(InvalidCommunities))]
    public async Task An_invalid_community_is_rejected(string? name, string? description)
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "community-invalid");

        await (await _client.PostJsonAsync("/api/v1/communities", new { name, description }, owner.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Joining_twice_changes_nothing()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "community-join-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, "community-join-member");
        var communityId = await Create(owner, Unique("Dwa razy"));

        await Join(member, communityId);
        Assert.Equal(HttpStatusCode.NoContent, (await Join(member, communityId)).StatusCode);

        Assert.Equal(2, (await Get(member, $"/api/v1/communities/{communityId}")).GetProperty("memberCount").GetInt64());

        // Leaving twice is fine as well.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}/membership", member.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}/membership", member.Token)).StatusCode);
        await (await Join(member, Guid.NewGuid())).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
    }

    /// <summary>Roles are handed out by the owner alone — otherwise a moderator would take the community over.</summary>
    [Fact]
    public async Task Only_the_owner_hands_out_roles()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "role-owner");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "role-moderator");
        var member = await factory.CreateLoggedInUserAsync(_client, "role-member");
        var outsider = await factory.CreateLoggedInUserAsync(_client, "role-outsider");
        var communityId = await Create(owner, Unique("Role"));
        await Join(moderator, communityId);
        await Join(member, communityId);

        Assert.Equal(HttpStatusCode.NoContent, (await SetRole(owner, communityId, moderator.Id, "MODERATOR")).StatusCode);
        Assert.Equal("MODERATOR", (await Get(moderator, $"/api/v1/communities/{communityId}")).GetProperty("myRole").GetString());

        await (await SetRole(moderator, communityId, member.Id, "MODERATOR")).ReadErrorAsync(HttpStatusCode.Forbidden, "NOT_COMMUNITY_OWNER");

        // Not even by their own hand: that would leave the community without an owner.
        await (await SetRole(owner, communityId, owner.Id, "MEMBER")).ReadErrorAsync(HttpStatusCode.Forbidden, "CANNOT_MODERATE_OWNER");
        await (await SetRole(owner, communityId, member.Id, "OWNER")).ReadErrorAsync(HttpStatusCode.BadRequest, "CANNOT_ASSIGN_OWNER_ROLE");
        await (await SetRole(owner, communityId, outsider.Id, "MODERATOR")).ReadErrorAsync(HttpStatusCode.NotFound, "NOT_COMMUNITY_MEMBER");
        await (await _client.PutJsonAsync($"/api/v1/communities/{communityId}/members/{member.Id}/role", new { }, owner.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        Assert.Equal(HttpStatusCode.NoContent, (await SetRole(owner, communityId, moderator.Id, "MEMBER")).StatusCode);
        Assert.Equal("MEMBER", (await Get(moderator, $"/api/v1/communities/{communityId}")).GetProperty("myRole").GetString());
    }

    [Fact]
    public async Task The_membership_status_describes_the_caller()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "memstatus-owner");
        var outsider = await factory.CreateLoggedInUserAsync(_client, "memstatus-outsider");
        var communityId = await Create(owner, Unique("Memstatus"));

        var mine = await Get(owner, $"/api/v1/communities/{communityId}/membership");
        Assert.Equal("MEMBER", mine.GetProperty("status").GetString());
        Assert.Equal("OWNER", mine.GetProperty("role").GetString());
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("joinedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("ban").ValueKind);

        var theirs = await Get(outsider, $"/api/v1/communities/{communityId}/membership");
        Assert.Equal("NONE", theirs.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, theirs.GetProperty("role").ValueKind);

        await (await _client.GetAsync($"/api/v1/communities/{Guid.NewGuid()}/membership", outsider.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
    }

    [Fact]
    public async Task Mine_lists_the_newest_membership_first()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "community-mine-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, "community-mine-member");
        var first = await Create(owner, Unique("Pierwsza"));
        var second = await Create(owner, Unique("Druga"));
        await Join(member, first);
        await Join(member, second);

        var mine = (await Get(member, "/api/v1/communities/mine")).GetProperty("content").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();

        Assert.Equal([second, first], mine);
    }

    /// <summary>The account takes its memberships along, and the communities it owns go whole.</summary>
    [Fact]
    public async Task Deleting_an_account_removes_its_memberships_and_owned_communities()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "community-delete-owner");
        var leaving = await factory.CreateLoggedInUserAsync(_client, "community-delete-leaving");
        var stays = await Create(owner, Unique("Zostaje"));
        await Join(leaving, stays);
        var goes = await Create(leaving, Unique("Znika"));
        await Join(owner, goes);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(leaving)).StatusCode);

        Assert.Equal(1, (await Get(owner, $"/api/v1/communities/{stays}")).GetProperty("memberCount").GetInt64());
        await (await _client.GetAsync($"/api/v1/communities/{goes}", owner.Token)).ReadErrorAsync(HttpStatusCode.NotFound, "COMMUNITY_NOT_FOUND");
    }

    /// <summary>The boundary content and notifications will use.</summary>
    [Fact]
    public async Task The_membership_contract_answers_who_may_do_what()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "contract-owner");
        var moderator = await factory.CreateLoggedInUserAsync(_client, "contract-moderator");
        var member = await factory.CreateLoggedInUserAsync(_client, "contract-member");
        var outsider = await factory.CreateLoggedInUserAsync(_client, "contract-outsider");
        var name = Unique("Kontrakt");
        var communityId = await Create(owner, name);
        await Join(moderator, communityId);
        await Join(member, communityId);
        await SetRole(owner, communityId, moderator.Id, "MODERATOR");
        var ct = TestContext.Current.CancellationToken;

        using var scope = factory.Services.CreateScope();
        var membership = scope.ServiceProvider.GetRequiredService<ICommunityMembership>();
        var directory = scope.ServiceProvider.GetRequiredService<ICommunityDirectory>();

        Assert.True(await membership.ExistsAsync(communityId, ct));
        Assert.False(await membership.ExistsAsync(Guid.NewGuid(), ct));
        Assert.True(await membership.IsMemberAsync(communityId, member.Id, ct));
        Assert.False(await membership.IsMemberAsync(communityId, outsider.Id, ct));
        Assert.True(await membership.IsOwnerAsync(communityId, owner.Id, ct));
        Assert.False(await membership.IsOwnerAsync(communityId, moderator.Id, ct));
        Assert.True(await membership.CanModerateAsync(communityId, owner.Id, ct));
        Assert.True(await membership.CanModerateAsync(communityId, moderator.Id, ct));
        Assert.False(await membership.CanModerateAsync(communityId, member.Id, ct));
        Assert.Equal(new HashSet<Guid> { owner.Id, moderator.Id }, await membership.ModeratorIdsAsync(communityId, ct));
        Assert.Equal(name, await directory.NameOfAsync(communityId, ct));
        Assert.Null(await directory.NameOfAsync(Guid.NewGuid(), ct));
    }

    [Theory]
    [InlineData("/api/v1/communities")]
    [InlineData("/api/v1/communities/mine")]
    public async Task Community_endpoints_require_authentication(string path) =>
        await (await _client.GetAsync(path, accessToken: null)).ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}"[..Math.Min(100, name.Length + 33)];

    private async Task<Guid> Create(LoggedInUser owner, string name)
    {
        var response = await _client.PostJsonAsync("/api/v1/communities", new { name }, owner.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Join(LoggedInUser user, Guid communityId) =>
        _client.PutJsonAsync($"/api/v1/communities/{communityId}/membership", new { }, user.Token);

    private Task<HttpResponseMessage> SetRole(LoggedInUser requester, Guid communityId, Guid userId, string role) =>
        _client.PutJsonAsync($"/api/v1/communities/{communityId}/members/{userId}/role", new { role }, requester.Token);

    private Task<HttpResponseMessage> DeleteAccount(LoggedInUser user)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/me") { Content = JsonContent.Create(new { password = user.User.Password }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> Get(LoggedInUser user, string path) => await (await _client.GetAsync(path, user.Token)).ReadJsonAsync();

    private static Task<JsonElement> Json(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
}
