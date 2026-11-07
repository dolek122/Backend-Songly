using System.Net;
using Songly.IntegrationTests.Community;
using Songly.IntegrationTests.Content;

namespace Songly.IntegrationTests.Notification;

/// <summary>
/// The push channel (original ADR 0040): who gets a push, what is in it and when a device stops getting
/// them. Ported from the original <c>PushNotificationIntegrationTest</c>. Device tokens are unique per test —
/// the database and the recorder are shared by the whole suite.
/// </summary>
public sealed class PushNotificationTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly ContentTestClient _content = new(factory.CreateClient());

    private RecordingPushSender Pushes => factory.Pushes;

    /// <summary>A like rings the author's phone with the liker's name and nothing more: no post text, the type and ids for the app to open it.</summary>
    [Fact]
    public async Task A_like_sends_a_push_naming_the_actor_but_nothing_more()
    {
        var author = await UserAsync("push-like-author");
        var fan = await UserAsync("push-like-fan");
        var device = await DeviceAsync(author);
        var postId = await _content.PublishAsync(author, "tajny tekst posta");

        await _content.LikeAsync(fan, postId);

        var push = Assert.Single(Pushes.SentTo(device));
        Assert.Equal(NameOf(fan), push.Title);
        Assert.Equal("Polubienie Twojego posta", push.Body);
        Assert.Equal("POST_LIKED", push.Data["type"]);
        Assert.Equal(postId.ToString(), push.Data["resourceId"]);
        Assert.True(Guid.TryParse(push.Data["notificationId"], out _));
        Assert.DoesNotContain("tajny", push.Title + push.Body + string.Join(' ', push.Data.Values), StringComparison.Ordinal);
    }

    /// <summary>A comment says there is one, not what it says — the lock screen is the most public place there is.</summary>
    [Fact]
    public async Task A_comment_push_does_not_carry_the_comment()
    {
        var author = await UserAsync("push-comment-author");
        var talker = await UserAsync("push-comment-talker");
        var device = await DeviceAsync(author);

        await _content.CommentAsync(talker, await _content.PublishAsync(author), "prywatna uwaga");

        var push = Assert.Single(Pushes.SentTo(device));
        Assert.Equal("Nowy komentarz pod Twoim postem", push.Body);
        Assert.DoesNotContain("prywatna", push.Title + push.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A suspension does not unpin the phones, and a suspended account has no access to anything a push leads
    /// to (original ADR 0022). Without the filter the lock screen kept showing the names of people inviting it
    /// (ADR 0049).
    /// </summary>
    [Fact]
    public async Task A_suspended_recipient_gets_no_push()
    {
        var suspended = await UserAsync("push-suspended-recipient");
        var sender = await UserAsync("push-suspended-sender");
        var device = await DeviceAsync(suspended);
        await factory.SuspendAsync(suspended.User);

        Assert.Equal(HttpStatusCode.Created, (await _client.PostJsonAsync("/api/v1/friends/requests", new { addresseeId = suspended.Id }, sender.Token)).StatusCode);

        Assert.Empty(Pushes.SentTo(device));
    }

    /// <summary>The inbox's deduplication (original ADR 0035) holds for push: an undo/redo loop must not ring someone's pocket at every turn.</summary>
    [Fact]
    public async Task A_like_loop_does_not_repeat_the_push()
    {
        var author = await UserAsync("push-loop-author");
        var fan = await UserAsync("push-loop-fan");
        var device = await DeviceAsync(author);
        var postId = await _content.PublishAsync(author);

        for (var round = 0; round < 3; round++)
        {
            await _content.LikeAsync(fan, postId);
            await _content.UnlikeAsync(fan, postId);
        }

        Assert.Single(Pushes.SentTo(device));
    }

    /// <summary>
    /// The block filter works at the moment of sending, but a moderation decision gets through it — or blocking
    /// the moderator would be enough not to learn of one's own ban (original ADR 0012). And it carries the
    /// community, not the moderator.
    /// </summary>
    [Fact]
    public async Task A_block_silences_activity_but_not_moderation_decisions()
    {
        var owner = await UserAsync("push-block-owner");
        var troll = await UserAsync("push-block-troll");
        var ownerDevice = await DeviceAsync(owner);
        var trollDevice = await DeviceAsync(troll);
        var communities = new CommunityTestClient(factory, _client);
        var communityId = await communities.CreateAsync(owner, "Push-blokady");
        await _content.BlockAsync(owner, troll);

        await communities.JoinedAsync(troll, communityId);
        Assert.Empty(Pushes.SentTo(ownerDevice));

        await communities.BannedAsync(owner, communityId, troll.Id, reason: "spam");

        var push = Assert.Single(Pushes.SentTo(trollDevice));
        Assert.Equal($"„{await CommunityNameAsync(owner, communityId)}”", push.Title);
        Assert.DoesNotContain(NameOf(owner), push.Title + push.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("spam", push.Title + push.Body, StringComparison.Ordinal);
    }

    /// <summary>The same phone, another account: the previous owner must not keep getting their pushes there.</summary>
    [Fact]
    public async Task Registering_a_known_token_moves_the_device_to_the_new_account()
    {
        var first = await UserAsync("push-move-first");
        var second = await UserAsync("push-move-second");
        var fan = await UserAsync("push-move-fan");
        var device = await DeviceAsync(first);
        await RegisterAsync(second, device);

        await _content.LikeAsync(fan, await _content.PublishAsync(first));

        Assert.Empty(Pushes.SentTo(device));
    }

    /// <summary>Someone else's token gives a 204 but unpins nothing — the answer does not say whose token it is. The owner's own removal does.</summary>
    [Fact]
    public async Task Removing_someone_elses_token_has_no_effect()
    {
        var owner = await UserAsync("push-rm-owner");
        var stranger = await UserAsync("push-rm-stranger");
        var fan = await UserAsync("push-rm-fan");
        var device = await DeviceAsync(owner);

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(stranger, device)).StatusCode);
        await _content.LikeAsync(fan, await _content.PublishAsync(owner));
        Assert.Single(Pushes.SentTo(device));

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(owner, device)).StatusCode);
        await _content.LikeAsync(fan, await _content.PublishAsync(owner));
        Assert.Single(Pushes.SentTo(device));
    }

    /// <summary>A password change cuts the phones off with the sessions — otherwise a stolen phone would keep ringing.</summary>
    [Fact]
    public async Task A_password_change_unregisters_every_device()
    {
        var owner = await UserAsync("push-pwd-owner");
        var device = await DeviceAsync(owner);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/auth/password", new { currentPassword = owner.User.Password, newPassword = "newPassword456" }, owner.Token)).StatusCode);

        Assert.Equal(0L, await DevicesAsync(device));
    }

    /// <summary>A reset revokes every session too, so it unpins the phones the same way.</summary>
    [Fact]
    public async Task A_password_reset_unregisters_every_device()
    {
        var owner = await UserAsync("push-reset-owner");
        var device = await DeviceAsync(owner);
        await _client.PostJsonAsync("/api/v1/auth/password/reset-request", new { email = owner.User.Email });

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/v1/auth/password/reset", new { token = factory.Mails.TokenSentTo(owner.User.Email), newPassword = "resetPassword789" })).StatusCode);

        Assert.Equal(0L, await DevicesAsync(device));
    }

    /// <summary>A stolen refresh token detected revokes every session — and the phones go with them, on the path that commits despite the 401.</summary>
    [Fact]
    public async Task A_detected_token_theft_unregisters_every_device()
    {
        var owner = await UserAsync("push-theft-owner");
        var device = await DeviceAsync(owner);
        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(owner.Tokens.RefreshToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(owner.Tokens.RefreshToken)).StatusCode);

        Assert.Equal(0L, await DevicesAsync(device));
    }

    /// <summary>A device FCM no longer knows disappears — later pushes do not go into the void.</summary>
    [Fact]
    public async Task An_unregistered_device_is_forgotten()
    {
        var author = await UserAsync("push-gone-author");
        var fan = await UserAsync("push-gone-fan");
        var device = await DeviceAsync(author);
        Pushes.MarkUnregistered(device);

        Assert.Equal(HttpStatusCode.NoContent, (await _content.LikeAsync(fan, await _content.PublishAsync(author))).StatusCode);

        Assert.Equal(0L, await DevicesAsync(device));
    }

    /// <summary>
    /// The surplus over the limit is trimmed, least recently registered first — otherwise the table grows with
    /// every reinstall and every push also goes to phones nobody has. Registering again counts as recent.
    /// </summary>
    [Fact]
    public async Task Devices_above_the_limit_are_trimmed_oldest_first()
    {
        var owner = await UserAsync("push-limit-owner");
        var devices = new List<string>();
        for (var i = 0; i < 11; i++)
        {
            devices.Add(await DeviceAsync(owner));
        }

        Assert.Equal(0L, await DevicesAsync(devices[0]));
        Assert.Equal(10L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM push_devices WHERE user_id = @id", ("id", owner.Id)));

        await RegisterAsync(owner, devices[1]);
        await DeviceAsync(owner);
        Assert.Equal(1L, await DevicesAsync(devices[1]));
        Assert.Equal(0L, await DevicesAsync(devices[2]));
    }

    [Fact]
    public async Task A_device_needs_a_token_and_a_platform()
    {
        var owner = await UserAsync("push-validation");

        await (await _client.PutJsonAsync("/api/v1/notifications/push-devices", new { token = "", platform = "ANDROID" }, owner.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await _client.PutJsonAsync("/api/v1/notifications/push-devices", new { token = Guid.NewGuid().ToString() }, owner.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await _client.PutJsonAsync("/api/v1/notifications/push-devices", new { token = new string('t', 513), platform = "IOS" }, owner.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary><c>push_devices</c> points at <c>users</c>, so its rows go with the account.</summary>
    [Fact]
    public async Task Deleting_the_account_removes_its_devices()
    {
        var leaving = await UserAsync("push-leaving");
        var device = await DeviceAsync(leaving);

        Assert.Equal(HttpStatusCode.NoContent, (await new CommunityTestClient(factory, _client).DeleteAccountAsync(leaving)).StatusCode);

        Assert.Equal(0L, await DevicesAsync(device));
    }

    private Task<LoggedInUser> UserAsync(string prefix) => factory.CreateLoggedInUserAsync(_client, prefix);

    private async Task<string> DeviceAsync(LoggedInUser user)
    {
        var token = $"device-{Guid.NewGuid():N}";
        await RegisterAsync(user, token);
        return token;
    }

    private async Task RegisterAsync(LoggedInUser user, string token) =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/notifications/push-devices", new { token, platform = "ANDROID" }, user.Token)).StatusCode);

    private Task<HttpResponseMessage> RemoveAsync(LoggedInUser user, string token) =>
        _client.PostJsonAsync("/api/v1/notifications/push-devices/removal", new { token }, user.Token);

    private Task<long> DevicesAsync(string token) =>
        factory.QuerySingleAsync<long>("SELECT count(*) FROM push_devices WHERE token = @token", ("token", token));

    private async Task<string> CommunityNameAsync(LoggedInUser viewer, Guid communityId)
    {
        using var response = await _client.GetAsync($"/api/v1/communities/{communityId}", viewer.Token);
        return (await Profile.ProfileTestExtensions.ReadJsonAsync(response)).GetProperty("name").GetString()!;
    }

    private static string NameOf(LoggedInUser user) => user.User.Email[..user.User.Email.IndexOf('@', StringComparison.Ordinal)];
}
