using System.Net;
using System.Text.Json;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;
using Songly.IntegrationTests.Profile;
using Songly.Tests.Shared;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// An account with data in every module at once, deleted by its owner. Ported from the original
/// <c>AccountDeletionFlowIntegrationTest</c>, with the moderation history added.
/// </summary>
/// <remarks>
/// No table has a foreign key to <c>users</c> with <c>ON DELETE CASCADE</c>, so the most likely place for an
/// event-driven deletion to fail is a module that forgot its handler — or ran it in the wrong order. Each module's
/// own tests delete only its own data; only here do they all meet in one transaction. The 204 is the main
/// assertion: a forgotten handler is a 500 from a foreign key.
/// </remarks>
public sealed class AccountDeletionFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ReportTestClient _reports = new(factory, factory.CreateClient());

    private CommunityTestClient Communities => _reports.Communities;

    private ContentTestClient Content => _reports.Content;

    [Fact]
    public async Task Deleting_an_account_removes_its_data_across_every_module()
    {
        var leaving = await factory.CreateLoggedInUserAsync(_client, "flow-leaving");
        var staying = await factory.CreateLoggedInUserAsync(_client, "flow-staying");
        var stranger = await factory.CreateLoggedInUserAsync(_client, "flow-stranger");

        // profile: favourite genres.
        var genreId = (await (await _client.GetAsync("/api/v1/genres?size=2", leaving.Token)).ReadJsonAsync()).GetProperty("content")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { genreId } }, leaving.Token)).StatusCode);

        // content: an own post, and a like, a comment and a share on someone else's.
        var ownPostId = await Content.PublishAsync(leaving, "post, który zaraz zniknie");
        var otherPostId = await Content.PublishAsync(staying, "post kogoś innego");
        Assert.Equal(HttpStatusCode.NoContent, (await Content.LikeAsync(leaving, otherPostId)).StatusCode);
        await Content.CommentedAsync(leaving, otherPostId, "komentarz przed usunięciem konta");
        Assert.Equal(HttpStatusCode.NoContent, (await Content.ShareAsync(leaving, otherPostId)).StatusCode);

        // The other way round too: a notification whose actor stays and whose post goes.
        Assert.Equal(HttpStatusCode.NoContent, (await Content.LikeAsync(staying, ownPostId)).StatusCode);

        // social: a friendship, and a pending request to a third person.
        await Content.BefriendAsync(leaving, staying);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostJsonAsync("/api/v1/friends/requests", new { addresseeId = stranger.Id }, leaving.Token)).StatusCode);

        // community: an own community others joined, with a ban the leaving owner issued.
        var ownCommunityId = await Communities.CreateAsync(leaving, "flow-own");
        await Communities.JoinedAsync(staying, ownCommunityId);
        await Communities.JoinedAsync(stranger, ownCommunityId);
        await Communities.BannedAsync(leaving, ownCommunityId, stranger.Id);

        // community and moderation log: a moderator in someone else's community — an entry about the leaving user
        // (the role) and one by them (the ban), which outlives them without its author.
        var otherCommunityId = await Communities.CreateAsync(staying, "flow-other");
        await Communities.JoinedAsync(leaving, otherCommunityId);
        await Communities.JoinedAsync(stranger, otherCommunityId);
        Assert.Equal(HttpStatusCode.NoContent, (await Communities.SetRoleAsync(staying, otherCommunityId, leaving.Id, "MODERATOR")).StatusCode);
        await Communities.BannedAsync(leaving, otherCommunityId, stranger.Id);

        // content: a report filed by the leaving user, which goes with them.
        await _reports.ReportedAsync(leaving, await Content.PublishedInAsync(staying, otherCommunityId, "post do zgłoszenia"));

        // content: an image uploaded and never attached to a post — a row in the register of uploads.
        Assert.Equal(HttpStatusCode.OK, (await new PostImageClient(_client).UploadAsync(leaving, TestImages.Png())).StatusCode);

        // now-playing: the visibility setting and a manual status.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/now-playing/settings", new { shareWithFriends = false }, leaving.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/now-playing/manual", new { title = "Ostatnia piosenka" }, leaving.Token)).StatusCode);

        // notification: mail preferences and a push device — rows that exist only once set.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/notifications/mail-preferences", new { decisionsAboutMe = false, moderationQueue = false }, leaving.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/notifications/push-devices", new { token = $"flow-{Guid.NewGuid():N}", platform = "ANDROID" }, leaving.Token)).StatusCode);

        // identity: an unused password reset token.
        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostJsonAsync("/api/v1/auth/password/reset-request", new { email = leaving.User.Email })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Communities.DeleteAccountAsync(leaving)).StatusCode);

        // The account is gone…
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync(leaving.User.Email, leaving.User.Password)).StatusCode);

        // …and so is its data, without breaking anyone else's.
        Assert.Equal(0, (await (await _client.GetAsync("/api/v1/friends", staying.Token)).ReadJsonAsync()).GetProperty("content").GetArrayLength());
        Assert.Equal(0, (await (await _client.GetAsync("/api/v1/friends/requests?direction=INCOMING", stranger.Token)).ReadJsonAsync()).GetProperty("content").GetArrayLength());
        var otherPost = await Content.PostAsync(staying, otherPostId);
        Assert.Equal(0, otherPost.GetProperty("likeCount").GetInt32());
        Assert.Equal(0, otherPost.GetProperty("commentCount").GetInt32());
        Assert.Equal(0, otherPost.GetProperty("shareCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await Content.GetPostAsync(staying, ownPostId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/v1/communities/{ownCommunityId}", staying.Token)).StatusCode);

        // The ban the leaving moderator issued stays, and keeps its entry in the history — without an author.
        Assert.Single((await Communities.BansAsync(staying, otherCommunityId)).EnumerateArray());
        var history = (await (await _client.GetAsync($"/api/v1/communities/{otherCommunityId}/moderation-log", staying.Token)).ReadJsonAsync()).GetProperty("content");
        var ban = Assert.Single(history.EnumerateArray());
        Assert.Equal("COMMUNITY_BAN_ISSUED", ban.GetProperty("action").GetString());
        Assert.Equal(stranger.Id, ban.GetProperty("subjectId").GetGuid());
        Assert.Equal(JsonValueKind.Null, ban.GetProperty("actorId").ValueKind);
    }
}
