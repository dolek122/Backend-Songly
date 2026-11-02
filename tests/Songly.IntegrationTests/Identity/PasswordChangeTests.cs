using System.Net;

namespace Songly.IntegrationTests.Identity;

/// <summary>Ported from the original <c>PasswordChangeIntegrationTest</c> and <c>PasswordByteLimitIntegrationTest</c>.</summary>
public sealed class PasswordChangeTests(SonglyApiFactory factory)
{
    private const string NewPassword = "brand-new-password";

    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> ChangePassword(string accessToken, string current, string replacement) =>
        _client.PutJsonAsync("/api/v1/auth/password", new { currentPassword = current, newPassword = replacement }, accessToken);

    /// <summary>
    /// The heart of it: the sessions of <b>both</b> devices die, not just the one that asked. A
    /// change that leaves the thief a working refresh token is an illusion of security.
    /// </summary>
    [Fact]
    public async Task Changing_the_password_kills_the_sessions_of_every_device()
    {
        var user = await factory.CreateUserAsync("pwd-change");
        var phone = await _client.LoginAsync(user);
        var laptop = await _client.LoginAsync(user);

        Assert.Equal(HttpStatusCode.NoContent, (await ChangePassword(phone.AccessToken, user.Password, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(phone.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(laptop.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task After_the_change_only_the_new_password_logs_in()
    {
        var user = await factory.CreateUserAsync("pwd-new-only");
        var tokens = await _client.LoginAsync(user);

        await ChangePassword(tokens.AccessToken, user.Password, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, NewPassword)).StatusCode);
    }

    /// <summary>
    /// A valid access token alone is not enough — otherwise stealing one would let the thief set
    /// their own password and keep the account for good.
    /// </summary>
    [Fact]
    public async Task Wrong_current_password_changes_nothing_and_leaves_sessions_alive()
    {
        var user = await factory.CreateUserAsync("pwd-wrong");
        var tokens = await _client.LoginAsync(user);

        await (await ChangePassword(tokens.AccessToken, "not-my-password", NewPassword)).ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");

        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(tokens.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
    }

    [Fact]
    public async Task Changing_to_the_same_password_is_rejected_and_keeps_sessions()
    {
        var user = await factory.CreateUserAsync("pwd-same");
        var tokens = await _client.LoginAsync(user);

        await (await ChangePassword(tokens.AccessToken, user.Password, user.Password)).ReadErrorAsync(HttpStatusCode.BadRequest, "NEW_PASSWORD_SAME_AS_CURRENT");

        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(tokens.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task A_short_new_password_is_rejected_by_validation()
    {
        var user = await factory.CreateUserAsync("pwd-short");
        var tokens = await _client.LoginAsync(user);

        await (await ChangePassword(tokens.AccessToken, user.Password, "krotkie")).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Over bcrypt's 72 bytes while under 72 characters: a 400, not a silently truncated password.</summary>
    [Fact]
    public async Task A_new_password_over_the_bcrypt_byte_limit_is_rejected()
    {
        var user = await factory.CreateUserAsync("pwd-bytes");
        var tokens = await _client.LoginAsync(user);

        await (await ChangePassword(tokens.AccessToken, user.Password, new string('ż', 40))).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task The_endpoint_requires_authentication()
    {
        var response = await _client.PutJsonAsync("/api/v1/auth/password", new { currentPassword = "password123", newPassword = NewPassword });

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    /// <summary>
    /// A pending reset link usually exists because someone else requested it; the owner reacting
    /// with a password change must close it too.
    /// </summary>
    [Fact]
    public async Task Changing_the_password_invalidates_a_pending_reset_link()
    {
        var user = await factory.CreateUserAsync("pwd-reset-link");
        var tokens = await _client.LoginAsync(user);
        await factory.ExecuteSqlAsync(
            "INSERT INTO password_reset_tokens (id, user_id, token_hash, created_at, expires_at) VALUES (gen_random_uuid(), @id, @hash, now(), now() + interval '30 minutes')",
            ("id", user.Id),
            ("hash", Guid.NewGuid().ToString()));

        await ChangePassword(tokens.AccessToken, user.Password, NewPassword);

        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM password_reset_tokens WHERE user_id = @id", ("id", user.Id)));
    }

    /// <summary>
    /// The access token lives on until it expires — JWTs are stateless by design, so revocation
    /// cuts off renewal, not the remaining minutes. Recorded here so nobody mistakes it for a hole
    /// without reading the decision.
    /// </summary>
    [Fact]
    public async Task The_access_token_stays_valid_until_it_expires_by_design()
    {
        var user = await factory.CreateUserAsync("pwd-access");
        var tokens = await _client.LoginAsync(user);

        await ChangePassword(tokens.AccessToken, user.Password, NewPassword);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/probe-secured/whoami", tokens.AccessToken)).StatusCode);
    }
}
