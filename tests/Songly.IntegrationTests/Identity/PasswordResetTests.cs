using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Mail;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Password reset — the only way back into an account for someone who forgot the password.
/// Ported from the original <c>PasswordResetIntegrationTest</c>, <c>PasswordResetThrottleIntegrationTest</c>,
/// <c>EmailNormalizationIntegrationTest</c> and <c>PasswordByteLimitIntegrationTest</c>.
/// </summary>
/// <remarks>
/// Integration tests, because the heart of the feature lies outside the service: the token exists
/// in plain form only in the sent message, and the effect of a reset — every session revoked — is
/// a bulk update that only a real database can show.
/// </remarks>
public sealed class PasswordResetTests(SonglyApiFactory factory)
{
    private const string NewPassword = "brand-new-password";

    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> RequestReset(string email, HttpClient? client = null) =>
        (client ?? _client).PostJsonAsync("/api/v1/auth/password/reset-request", new { email });

    private Task<HttpResponseMessage> Reset(string token, string newPassword = NewPassword, HttpClient? client = null) =>
        (client ?? _client).PostJsonAsync("/api/v1/auth/password/reset", new { token, newPassword });

    /// <summary>The path a user takes: request a link, get the e-mail, set a new password, get back in.</summary>
    [Fact]
    public async Task The_full_flow_lets_the_user_back_in_with_the_new_password()
    {
        var user = await factory.CreateUserAsync("reset-happy");

        Assert.Equal(HttpStatusCode.Accepted, (await RequestReset(user.Email)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Reset(factory.Mails.TokenSentTo(user.Email))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, NewPassword)).StatusCode);
    }

    /// <summary>
    /// A reset follows a loss of control over the account, so it has the effect of a password
    /// change: the sessions of every device die. Otherwise the thief keeps a valid refresh token.
    /// </summary>
    [Fact]
    public async Task A_reset_kills_the_sessions_of_every_device()
    {
        var user = await factory.CreateUserAsync("reset-sessions");
        var phone = await _client.LoginAsync(user);
        var laptop = await _client.LoginAsync(user);

        await RequestReset(user.Email);
        await Reset(factory.Mails.TokenSentTo(user.Email));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(phone.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(laptop.RefreshToken)).StatusCode);
    }

    /// <summary>
    /// An unknown address looks exactly like a known one, and nothing is sent: a 202 alone would
    /// not rule out a message, and sending anything to an unregistered address is unwanted anyway.
    /// </summary>
    [Fact]
    public async Task An_unknown_address_looks_identical_and_sends_nothing()
    {
        var known = await factory.CreateUserAsync("reset-known");
        var unknown = TestAccounts.UniqueEmail("reset-nobody");

        var knownResponse = await RequestReset(known.Email);
        var unknownResponse = await RequestReset(unknown);

        Assert.Equal(knownResponse.StatusCode, unknownResponse.StatusCode);
        Assert.Equal(
            await knownResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await unknownResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(factory.Mails.SentTo(unknown));
    }

    /// <summary>A link lying in a mailbox or browser history must not open the account again and again.</summary>
    [Fact]
    public async Task A_token_works_only_once()
    {
        var user = await factory.CreateUserAsync("reset-once");
        await RequestReset(user.Email);
        var token = factory.Mails.TokenSentTo(user.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await Reset(token)).StatusCode);

        await (await Reset(token, "yet-another-password")).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_PASSWORD_RESET_TOKEN");
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, NewPassword)).StatusCode);
    }

    /// <summary>A distinct "expired" would confirm that such a token once existed.</summary>
    [Fact]
    public async Task An_expired_token_is_rejected_like_a_fabricated_one()
    {
        var user = await factory.CreateUserAsync("reset-expired");
        const string token = "expired-reset-token-for-this-test-only";
        await factory.ExecuteSqlAsync(
            "INSERT INTO password_reset_tokens (id, user_id, token_hash, created_at, expires_at) VALUES (gen_random_uuid(), @id, @hash, now() - interval '2 hours', now() - interval '1 hour')",
            ("id", user.Id),
            ("hash", TestTokens.HashOf(token)));

        var expired = await (await Reset(token)).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_PASSWORD_RESET_TOKEN");
        var fabricated = await (await Reset("never-issued")).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_PASSWORD_RESET_TOKEN");

        Assert.Equal(fabricated.GetProperty("message").GetString(), expired.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
    }

    /// <summary>Another "I did not get the e-mail" must not leave another valid key in circulation.</summary>
    [Fact]
    public async Task A_new_request_invalidates_the_previous_link()
    {
        using var app = factory.WithSettings(("Songly:Security:PasswordReset:ResendInterval", "00:00:00"));
        var client = app.CreateClient();
        var user = await factory.CreateUserAsync("reset-relink");

        await RequestReset(user.Email, client);
        var first = app.MailsOf().TokenSentTo(user.Email);
        await RequestReset(user.Email, client);
        var second = app.MailsOf().TokenSentTo(user.Email);

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(first, client: client)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Reset(second, client: client)).StatusCode);
    }

    /// <summary>
    /// The owner changing the password the normal way closes a pending link — which a stranger
    /// most likely requested.
    /// </summary>
    [Fact]
    public async Task Changing_the_password_invalidates_a_pending_reset_link()
    {
        var user = await factory.CreateUserAsync("reset-vs-change");
        var tokens = await _client.LoginAsync(user);
        await RequestReset(user.Email);
        var pending = factory.Mails.TokenSentTo(user.Email);

        var change = await _client.PutJsonAsync("/api/v1/auth/password", new { currentPassword = user.Password, newPassword = "chosen-deliberately" }, tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(pending, "attackers-password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, "chosen-deliberately")).StatusCode);
    }

    /// <summary>A reset must not be a way around the password policy.</summary>
    [Theory]
    [InlineData("krotkie")]
    [InlineData("żżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżżż")]
    public async Task A_new_password_breaking_the_policy_is_rejected(string newPassword)
    {
        var user = await factory.CreateUserAsync("reset-policy");
        await RequestReset(user.Email);

        await (await Reset(factory.Mails.TokenSentTo(user.Email), newPassword)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
    }

    /// <summary>The whole path exists for someone who cannot log in.</summary>
    [Fact]
    public async Task Both_endpoints_are_reachable_without_authentication()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await RequestReset(TestAccounts.UniqueEmail("reset-anyone"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset("anything")).StatusCode);
    }

    [Fact]
    public async Task A_different_case_reaches_the_account()
    {
        var user = await factory.CreateUserAsync("reset-case");

        await RequestReset(user.Email.ToUpperInvariant());

        Assert.Equal("Songly — reset hasła", factory.Mails.LastTo(user.Email).Subject);
    }

    [Fact]
    public async Task The_message_links_to_the_app_and_says_how_long_the_link_lives()
    {
        var user = await factory.CreateUserAsync("reset-message");

        await RequestReset(user.Email);

        var body = factory.Mails.LastTo(user.Email).Body;
        Assert.Contains($"songly://reset-password?token={factory.Mails.TokenSentTo(user.Email)}", body, StringComparison.Ordinal);
        Assert.Contains("wygasa za 30 minut.", body, StringComparison.Ordinal);
    }

    /// <summary>The database holds only the hash; a leaked database must not reset anyone's password.</summary>
    [Fact]
    public async Task Only_the_hash_of_the_token_is_stored()
    {
        var user = await factory.CreateUserAsync("reset-hash");

        await RequestReset(user.Email);

        var stored = await factory.QuerySingleAsync<string>("SELECT token_hash FROM password_reset_tokens WHERE user_id = @id", ("id", user.Id));
        Assert.Equal(TestTokens.HashOf(factory.Mails.TokenSentTo(user.Email)), stored);
    }

    /// <summary>
    /// Repeated requests within the interval get the same 202 but no message. A distinguishable
    /// 429 would reveal that a reset was just requested for this address — that it has an account.
    /// </summary>
    [Fact]
    public async Task Repeated_requests_within_the_interval_send_one_message()
    {
        var user = await factory.CreateUserAsync("reset-throttle");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await RequestReset(user.Email)).StatusCode);
        }

        Assert.Single(factory.Mails.SentTo(user.Email));
    }

    /// <summary>
    /// A mail failure must not become a 500: it can only happen for addresses with an account, so
    /// the outage would be an oracle.
    /// </summary>
    [Fact]
    public async Task A_failing_mail_channel_does_not_change_the_response()
    {
        using var app = factory.WithSettings().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IMailSender, FailingMailSender>()));
        var user = await factory.CreateUserAsync("reset-mail-down");

        Assert.Equal(HttpStatusCode.Accepted, (await RequestReset(user.Email, app.CreateClient())).StatusCode);
    }

    /// <summary>
    /// Two requests with one link racing each other: exactly one sets the password. Otherwise a
    /// stolen link used at the same moment as the owner's would leave the result to chance.
    /// </summary>
    [Fact]
    public async Task Concurrent_resets_with_one_link_succeed_exactly_once()
    {
        var user = await factory.CreateUserAsync("reset-race");
        await RequestReset(user.Email);
        var token = factory.Mails.TokenSentTo(user.Email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(attempt => Reset(token, $"racing-password-{attempt}")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.NoContent), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }
}
