using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Songly.Common.Mail;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Registration with e-mail verification. Ported from the original
/// <c>EmailVerificationIntegrationTest</c>, <c>EmailNormalizationIntegrationTest</c> and
/// <c>PasswordByteLimitIntegrationTest</c>.
/// </summary>
public sealed class RegistrationTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private RecordingMailSender Mails => factory.Mails;

    /// <summary>The heart of it: until the address is confirmed, the account does not exist.</summary>
    [Fact]
    public async Task The_account_does_not_exist_until_the_address_is_confirmed()
    {
        var email = TestAccounts.UniqueEmail("verify-pending");

        Assert.Equal(HttpStatusCode.Accepted, (await _client.RegisterAsync(email)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync(email, TestAccounts.DefaultPassword)).StatusCode);
        Assert.Equal(0L, await CountUsers(email));

        Assert.Equal(HttpStatusCode.OK, (await _client.VerifyAsync(Mails.TokenSentTo(email))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(email, TestAccounts.DefaultPassword)).StatusCode);
        Assert.Equal(1L, await CountUsers(email));
    }

    /// <summary>
    /// Confirming logs the user in: clicking the link proves control of the mailbox, and the same
    /// person typed the password a moment ago.
    /// </summary>
    [Fact]
    public async Task Verification_logs_the_user_in_immediately()
    {
        var tokens = await _client.RegisterAndVerifyAsync(Mails, TestAccounts.UniqueEmail("verify-tokens"));

        Assert.NotEmpty(tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/probe-secured/whoami", tokens.AccessToken)).StatusCode);
    }

    /// <summary>
    /// A taken address looks exactly like a free one (202, empty body) and creates nothing. Only
    /// the mailbox owner sees a difference — in the content of the message.
    /// </summary>
    [Fact]
    public async Task Registering_a_taken_address_is_indistinguishable_but_warns_the_owner()
    {
        var owner = await factory.CreateUserAsync("verify-taken");
        var freeAddress = await _client.RegisterAsync(TestAccounts.UniqueEmail("verify-free"));

        var takenAddress = await _client.RegisterAsync(owner.Email, "another-password-123");

        Assert.Equal(freeAddress.StatusCode, takenAddress.StatusCode);
        Assert.Equal(
            await freeAddress.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await takenAddress.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var warning = Assert.Single(Mails.SentTo(owner.Email));
        Assert.Contains("ktoś próbował założyć konto", warning.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("token=", warning.Body, StringComparison.Ordinal);
        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM pending_registrations WHERE email = @email", ("email", owner.Email)));

        // The account keeps its password: registering on a taken address overwrites nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync(owner.Email, "another-password-123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(owner.Email, owner.Password)).StatusCode);
    }

    [Fact]
    public async Task A_verification_token_works_only_once()
    {
        var email = TestAccounts.UniqueEmail("verify-once");
        await _client.RegisterAsync(email);
        var token = Mails.TokenSentTo(email);

        Assert.Equal(HttpStatusCode.OK, (await _client.VerifyAsync(token)).StatusCode);

        await (await _client.VerifyAsync(token)).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_VERIFICATION_TOKEN");
    }

    /// <summary>A distinct "expired" would confirm that such a registration once existed.</summary>
    [Fact]
    public async Task An_expired_token_is_rejected_like_a_fabricated_one()
    {
        var email = TestAccounts.UniqueEmail("verify-expired");
        const string token = "expired-token-for-this-test-only";
        await factory.ExecuteSqlAsync(
            "INSERT INTO pending_registrations (id, email, password_hash, token_hash, created_at, expires_at) VALUES (gen_random_uuid(), @email, 'irrelevant', @hash, now() - interval '2 days', now() - interval '1 day')",
            ("email", email),
            ("hash", TestTokens.HashOf(token)));

        var expired = await (await _client.VerifyAsync(token)).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_VERIFICATION_TOKEN");
        var fabricated = await (await _client.VerifyAsync("never-issued")).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_VERIFICATION_TOKEN");

        Assert.Equal(fabricated.GetProperty("message").GetString(), expired.GetProperty("message").GetString());
        Assert.Equal(0L, await CountUsers(email));
    }

    /// <summary>
    /// The address got an account while the registration was waiting. The answer is the same as
    /// for a bad token — not the database's 409, which would tell the asker that the address has an
    /// account — and the stale registration is cleaned up.
    /// </summary>
    [Fact]
    public async Task A_registration_whose_address_meanwhile_got_an_account_is_rejected_like_a_bad_token()
    {
        var owner = await factory.CreateUserAsync("verify-meanwhile");
        const string token = "token-for-an-address-that-meanwhile-got-an-account";
        await factory.ExecuteSqlAsync(
            "INSERT INTO pending_registrations (id, email, password_hash, token_hash, created_at, expires_at) VALUES (gen_random_uuid(), @email, 'irrelevant', @hash, now(), now() + interval '1 day')",
            ("email", owner.Email),
            ("hash", TestTokens.HashOf(token)));

        await (await _client.VerifyAsync(token)).ReadErrorAsync(HttpStatusCode.BadRequest, "INVALID_VERIFICATION_TOKEN");

        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM pending_registrations WHERE email = @email", ("email", owner.Email)));
    }

    /// <summary>
    /// A repeated registration issues a new link and kills the old one — otherwise every "I did not
    /// get the e-mail" would leave another valid key in circulation. The password is <b>not</b>
    /// replaced: a stranger could otherwise inject their own password into someone else's pending
    /// registration and wait for the owner to click the newest link.
    /// </summary>
    [Fact]
    public async Task A_repeated_registration_reissues_the_link_but_keeps_the_original_password()
    {
        using var app = factory.WithSettings(("Songly:Security:Registration:ResendInterval", "00:00:00"));
        var client = app.CreateClient();
        var mails = app.MailsOf();
        var email = TestAccounts.UniqueEmail("verify-reissue");

        await client.RegisterAsync(email, "first-password");
        var firstToken = mails.TokenSentTo(email);
        await client.RegisterAsync(email, "attackers-password");
        var secondToken = mails.TokenSentTo(email);

        Assert.NotEqual(firstToken, secondToken);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.VerifyAsync(firstToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.VerifyAsync(secondToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.TryLoginAsync(email, "attackers-password")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.TryLoginAsync(email, "first-password")).StatusCode);
    }

    [Fact]
    public async Task The_verification_link_points_to_the_app()
    {
        var email = TestAccounts.UniqueEmail("verify-link");

        await _client.RegisterAsync(email);

        var mail = Mails.LastTo(email);
        Assert.Equal("Songly — potwierdź swój adres e-mail", mail.Subject);
        Assert.Contains($"songly://verify-email?token={Mails.TokenSentTo(email)}", mail.Body, StringComparison.Ordinal);
        Assert.Contains("Link wygasa po 24 h.", mail.Body, StringComparison.Ordinal);
    }

    /// <summary>The token is stored as a hash only; a leaked database must not confirm anyone's registration.</summary>
    [Fact]
    public async Task Only_the_hash_of_the_token_is_stored()
    {
        var email = TestAccounts.UniqueEmail("verify-hash");

        await _client.RegisterAsync(email);

        var stored = await factory.QuerySingleAsync<string>("SELECT token_hash FROM pending_registrations WHERE email = @email", ("email", email));
        Assert.Equal(TestTokens.HashOf(Mails.TokenSentTo(email)), stored);
    }

    [Fact]
    public async Task An_invalid_payload_is_rejected_by_validation()
    {
        var response = await _client.RegisterAsync("not-an-address", "short");

        await response.ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task A_password_over_the_bcrypt_byte_limit_is_rejected()
    {
        var response = await _client.RegisterAsync(TestAccounts.UniqueEmail("verify-bytes"), new string('ą', 40));

        await response.ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>An address registered with capitals logs in with any case (original ADR 0042).</summary>
    [Fact]
    public async Task An_address_registered_with_capitals_logs_in_with_any_case()
    {
        var email = TestAccounts.UniqueEmail("verify-case");
        var typed = "  " + char.ToUpperInvariant(email[0]) + email[1..] + " ";

        await _client.RegisterAsync(typed);
        await _client.VerifyAsync(Mails.TokenSentTo(email));

        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(email.ToUpperInvariant(), TestAccounts.DefaultPassword)).StatusCode);
    }

    [Fact]
    public async Task A_different_case_hits_the_existing_account()
    {
        var owner = await factory.CreateUserAsync("verify-case-taken");

        await _client.RegisterAsync(owner.Email.ToUpperInvariant());

        Assert.Contains("ktoś próbował założyć konto", Assert.Single(Mails.SentTo(owner.Email)).Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both branches hash the password, so a taken address costs the same time as a free one
    /// (original ADR 0009). Only a lower bound is asserted, so a slow machine cannot fail it.
    /// </summary>
    [Fact]
    public async Task Registering_a_taken_address_still_costs_a_bcrypt_hash()
    {
        var owner = await factory.CreateUserAsync("verify-timing");
        await _client.RegisterAsync(owner.Email);

        var timings = new List<TimeSpan>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            await _client.RegisterAsync(owner.Email);
            timings.Add(Stopwatch.GetElapsedTime(started));
        }

        Assert.True(timings.Min() >= TimeSpan.FromMilliseconds(15), $"Fastest registration on a taken address took {timings.Min().TotalMilliseconds} ms");
    }

    /// <summary>
    /// A mail failure must not become a 500: it could happen in only one of the two branches, and
    /// the outage would rebuild the oracle.
    /// </summary>
    [Fact]
    public async Task A_failing_mail_channel_does_not_change_the_response()
    {
        using var app = factory.WithSettings().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IMailSender, FailingMailSender>()));
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await client.RegisterAsync(TestAccounts.UniqueEmail("verify-mail-down"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.RegisterAsync((await factory.CreateUserAsync("verify-mail-down-taken")).Email)).StatusCode);
    }

    /// <summary>
    /// Two clicks on the same link racing each other: one account, and the other request fails as
    /// a client error — never a 500.
    /// </summary>
    [Fact]
    public async Task Concurrent_verifications_of_one_token_create_one_account()
    {
        var email = TestAccounts.UniqueEmail("verify-race");
        await _client.RegisterAsync(email);
        var token = Mails.TokenSentTo(email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _client.VerifyAsync(token)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses, r => Assert.True((int)r.StatusCode < 500, $"Got {(int)r.StatusCode}"));
        Assert.Equal(1L, await CountUsers(email));
    }

    private Task<long> CountUsers(string email) =>
        factory.QuerySingleAsync<long>("SELECT count(*) FROM users WHERE email = @email", ("email", email));
}
