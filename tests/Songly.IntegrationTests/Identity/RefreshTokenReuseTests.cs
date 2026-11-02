using System.Net;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Refresh-token reuse detection. Ported from the original <c>RefreshTokenReuseIntegrationTest</c>.
/// </summary>
/// <remarks>
/// These must be integration tests: what they check is transaction semantics, not control flow.
/// Revoking every session happens right before the error is returned — if a rollback undid it, a
/// test against mocks would still see the call and pass. The only thing that tells the two
/// versions apart is whether the token issued after rotation <b>really stops working</b>.
/// </remarks>
public sealed class RefreshTokenReuseTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>The heart of the feature: replaying a consumed token kills the session it was rotated into.</summary>
    [Fact]
    public async Task Replaying_a_consumed_token_kills_the_session_it_was_rotated_into()
    {
        var stolen = (await _client.LoginAsync(await factory.CreateUserAsync("reuse-core"))).RefreshToken;
        var rotated = (await (await _client.RefreshAsync(stolen)).ReadTokensAsync()).RefreshToken;

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(stolen)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(rotated)).StatusCode);
    }

    /// <summary>The attacker must not learn that their copy was detected.</summary>
    [Fact]
    public async Task Reuse_response_looks_exactly_like_any_other_bad_token()
    {
        var stolen = (await _client.LoginAsync(await factory.CreateUserAsync("reuse-opaque"))).RefreshToken;
        await (await _client.RefreshAsync(stolen)).ReadTokensAsync();

        var reuse = await (await _client.RefreshAsync(stolen)).ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
        var garbage = await (await _client.RefreshAsync("no-such-token")).ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");

        Assert.Equal(garbage.GetProperty("message").GetString(), reuse.GetProperty("message").GetString());
    }

    /// <summary>
    /// Every session, not just the chain where the abuse happened: it is unknown what else leaked
    /// with the token, so the second device goes down too.
    /// </summary>
    [Fact]
    public async Task Reuse_revokes_the_sessions_of_every_device()
    {
        var user = await factory.CreateUserAsync("reuse-devices");
        var phone = (await _client.LoginAsync(user)).RefreshToken;
        var laptop = (await _client.LoginAsync(user)).RefreshToken;

        await (await _client.RefreshAsync(phone)).ReadTokensAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(phone)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(laptop)).StatusCode);
    }

    /// <summary>The way out: the password the thief does not have.</summary>
    [Fact]
    public async Task After_reuse_the_user_recovers_by_logging_in_again()
    {
        var user = await factory.CreateUserAsync("reuse-recovery");
        var stolen = (await _client.LoginAsync(user)).RefreshToken;
        await (await _client.RefreshAsync(stolen)).ReadTokensAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(stolen)).StatusCode);

        var fresh = await _client.LoginAsync(user);

        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(fresh.RefreshToken)).StatusCode);
    }

    /// <summary>
    /// Logging out revokes a session, but it is not abuse. If a repeated logout counted as theft,
    /// every client retrying a logout after a dropped connection would kill the user's other
    /// devices.
    /// </summary>
    [Fact]
    public async Task Logging_out_twice_does_not_take_down_other_devices()
    {
        var user = await factory.CreateUserAsync("reuse-logout");
        var phone = (await _client.LoginAsync(user)).RefreshToken;
        var laptop = (await _client.LoginAsync(user)).RefreshToken;

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = phone })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = phone })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(laptop)).StatusCode);
    }

    /// <summary>
    /// Expiry is time passing, not theft. Counting it as reuse would log out everywhere everyone
    /// who comes back after a month. The session's age is set with SQL, because it is the subject.
    /// </summary>
    [Fact]
    public async Task An_expired_token_is_rejected_without_being_treated_as_reuse()
    {
        var user = await factory.CreateUserAsync("reuse-expired");
        var old = (await _client.LoginAsync(user)).RefreshToken;
        var current = (await _client.LoginAsync(user)).RefreshToken;
        await factory.ExecuteSqlAsync(
            "UPDATE sessions SET expires_at = now() - interval '1 day' WHERE refresh_token_hash = @hash",
            ("hash", TestTokens.HashOf(old)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(old)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(current)).StatusCode);
    }

    /// <summary>
    /// Two requests racing with the same token must not both win — that would hand the thief a
    /// second, undetected session chain. Exactly one rotates; the other is a second use of a
    /// consumed token.
    /// </summary>
    [Fact]
    public async Task Concurrent_refreshes_of_one_token_never_both_succeed()
    {
        var token = (await _client.LoginAsync(await factory.CreateUserAsync("reuse-race"))).RefreshToken;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _client.RefreshAsync(token)));

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1);
    }
}
