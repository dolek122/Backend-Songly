using System.Net;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// The profile is created in the transaction that creates the account. The original created it
/// after the commit, where a failure was only logged and left an account without a profile; here
/// both exist together or not at all.
/// </summary>
public sealed class ProfileOnRegistrationTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_module_failing_to_set_up_the_account_takes_the_account_down_and_the_link_still_works()
    {
        var email = TestAccounts.UniqueEmail("registration-rollback");
        Assert.Equal(HttpStatusCode.Accepted, (await _client.RegisterAsync(email)).StatusCode);
        var token = factory.Mails.TokenSentTo(email);
        ProbeRegistrationHandler.FailFor[email] = 0;

        await (await _client.VerifyAsync(token)).ReadErrorAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");

        // The profile module ran before the probe failed; its profile went with the account (a
        // profile cannot outlive its user row — the foreign key sees to that).
        Assert.Equal(0L, await factory.QuerySingleAsync<long>("SELECT count(*) FROM users WHERE email = @email", ("email", email)));

        // The pending registration was rolled back too, so the same link confirms the address.
        ProbeRegistrationHandler.FailFor.TryRemove(email, out _);
        var tokens = await (await _client.VerifyAsync(token)).ReadTokensAsync();
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/profiles/me", tokens.AccessToken)).StatusCode);
    }
}
