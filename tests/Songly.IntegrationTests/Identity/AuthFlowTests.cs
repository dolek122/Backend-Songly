using System.Net;
using System.Net.Http.Json;

namespace Songly.IntegrationTests.Identity;

/// <summary>Ported from the original <c>AuthFlowIntegrationTest</c> and <c>EmailNormalizationIntegrationTest</c>.</summary>
public sealed class AuthFlowTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_then_refresh_then_logout()
    {
        var user = await factory.CreateUserAsync("flow");

        var first = await _client.LoginAsync(user);
        var rotated = await (await _client.RefreshAsync(first.RefreshToken)).ReadTokensAsync();

        Assert.NotEmpty(rotated.AccessToken);
        Assert.NotEqual(first.RefreshToken, rotated.RefreshToken);
        await (await _client.RefreshAsync(first.RefreshToken)).ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");

        var logout = await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(rotated.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Token_response_has_the_original_shape()
    {
        var response = await _client.TryLoginAsync((await factory.CreateUserAsync("shape")).Email, TestAccounts.DefaultPassword);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Bearer", json.GetProperty("tokenType").GetString());
        Assert.Equal(900, json.GetProperty("expiresInSeconds").GetInt64());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Wrong_password_is_401_invalid_credentials()
    {
        var user = await factory.CreateUserAsync("wrong-password");

        var response = await _client.TryLoginAsync(user.Email, "wrong-password");

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    /// <summary>The response must not tell whether an address has an account.</summary>
    [Fact]
    public async Task Unknown_account_looks_exactly_like_a_wrong_password()
    {
        var user = await factory.CreateUserAsync("enumeration");

        var wrongPassword = await (await _client.TryLoginAsync(user.Email, "wrong-password")).ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
        var unknown = await (await _client.TryLoginAsync("nobody-here@songly.io", "wrong-password")).ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");

        Assert.Equal(wrongPassword.GetProperty("message").GetString(), unknown.GetProperty("message").GetString());
    }

    /// <summary>
    /// The same status is not enough if "no such account" answers faster than "wrong password":
    /// the response time alone would reveal which addresses have an account (original ADR 0041).
    /// So an unknown account still pays for a bcrypt verification at the application's cost.
    /// </summary>
    /// <remarks>
    /// Only a lower bound is asserted, so a slow machine cannot make it fail. bcrypt at cost 10
    /// takes tens of milliseconds on any hardware; without the dummy verification the same request
    /// is a single indexed query.
    /// </remarks>
    [Fact]
    public async Task An_unknown_account_still_costs_a_bcrypt_verification()
    {
        await _client.TryLoginAsync("warm-up@songly.io", "password123");

        var timings = new List<TimeSpan>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await _client.TryLoginAsync($"nobody-{attempt}@songly.io", "password123");
            timings.Add(System.Diagnostics.Stopwatch.GetElapsedTime(started));
        }

        Assert.True(timings.Min() >= TimeSpan.FromMilliseconds(15), $"Fastest unknown-account login took {timings.Min().TotalMilliseconds} ms");
    }

    /// <summary>
    /// A phone keyboard capitalizes the first letter and autocompletion adds a trailing space;
    /// neither may turn into "wrong password".
    /// </summary>
    [Fact]
    public async Task Email_is_normalized_at_login()
    {
        var user = await factory.CreateUserAsync("normalized");

        var response = await _client.TryLoginAsync($"  {user.Email.ToUpperInvariant()} ", user.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Blank_credentials_are_400_validation_error()
    {
        var response = await _client.TryLoginAsync(" ", "");

        await response.ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Logout_of_an_unknown_token_is_still_204()
    {
        var response = await _client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = "no-such-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
