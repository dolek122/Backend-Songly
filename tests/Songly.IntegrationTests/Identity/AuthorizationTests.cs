using System.Net;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Who may call what: closed by default, roles read from the live account state on every request,
/// a suspension enforced through roles, the admin area guarded by path.
/// </summary>
public sealed class AuthorizationTests(SonglyApiFactory factory)
{
    private const string SecuredProbe = "/probe-secured/whoami";

    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>An endpoint that declares nothing is closed — that is the default, not an opt-in.</summary>
    [Fact]
    public async Task An_endpoint_without_any_authorization_metadata_requires_a_token()
    {
        var response = await _client.GetAsync(SecuredProbe, accessToken: null);

        var error = await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        Assert.Equal("Wymagane uwierzytelnienie", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_valid_token_identifies_the_account()
    {
        var user = await factory.CreateUserAsync("whoami");
        var tokens = await _client.LoginAsync(user);

        var response = await _client.GetAsync(SecuredProbe, tokens.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(user.Id.ToString(), await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    public static TheoryData<string> RejectedTokens => new()
    {
        "not-a-jwt",
        "eyJhbGciOiJub25lIn0.eyJzdWIiOiJ4In0.",
    };

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task A_malformed_or_unsigned_token_is_401(string token)
    {
        var response = await _client.GetAsync(SecuredProbe, token);

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task An_expired_token_is_401_with_no_clock_skew()
    {
        var user = await factory.CreateUserAsync("expired-token");
        var token = TestTokens.Forge(factory, user.Id.ToString(), DateTime.UtcNow.AddSeconds(-5));

        var response = await _client.GetAsync(SecuredProbe, token);

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_401()
    {
        var user = await factory.CreateUserAsync("foreign-key");
        var token = TestTokens.Forge(factory, user.Id.ToString(), DateTime.UtcNow.AddMinutes(10), secret: "someone-elses-key-that-is-long-enough-1234");

        var response = await _client.GetAsync(SecuredProbe, token);

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_correctly_signed_token_without_a_subject_is_401()
    {
        var token = TestTokens.Forge(factory, subject: null, DateTime.UtcNow.AddMinutes(10));

        var response = await _client.GetAsync(SecuredProbe, token);

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    /// <summary>
    /// A suspended account gets its own code, so the app can explain the decision instead of
    /// looking broken (original ADR 0022).
    /// </summary>
    [Fact]
    public async Task A_suspended_account_is_403_account_suspended()
    {
        var user = await factory.CreateUserAsync("suspended");
        var tokens = await _client.LoginAsync(user);
        await factory.SuspendAsync(user);

        var response = await _client.GetAsync(SecuredProbe, tokens.AccessToken);

        await response.ReadErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    /// <summary>
    /// Logging in still works: a suspended account must be able to learn its state, appeal and
    /// delete itself (a GDPR right). What it cannot do is decided by roles, not by the login.
    /// </summary>
    [Fact]
    public async Task A_suspended_account_can_still_log_in()
    {
        var user = await factory.CreateUserAsync("suspended-login");
        await factory.SuspendAsync(user);

        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
    }

    /// <summary>Expiry is lazy: nothing wakes up when a suspension ends, it just stops counting.</summary>
    [Fact]
    public async Task An_expired_suspension_no_longer_restricts_the_account()
    {
        var user = await factory.CreateUserAsync("suspension-over");
        var tokens = await _client.LoginAsync(user);
        await factory.SuspendAsync(user, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(SecuredProbe, tokens.AccessToken)).StatusCode);
    }

    /// <summary>
    /// The role is read on every request, not baked into the token: suspending an account takes
    /// effect on its very next request, without touching any session (original ADR 0006).
    /// </summary>
    [Fact]
    public async Task A_suspension_takes_effect_on_the_next_request_with_the_same_token()
    {
        var user = await factory.CreateUserAsync("suspended-live");
        var tokens = await _client.LoginAsync(user);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(SecuredProbe, tokens.AccessToken)).StatusCode);

        await factory.SuspendAsync(user);

        await (await _client.GetAsync(SecuredProbe, tokens.AccessToken)).ReadErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task The_admin_area_is_401_without_a_token()
    {
        await (await _client.GetAsync("/api/v1/admin/probe", accessToken: null)).ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task The_admin_area_is_403_forbidden_for_a_regular_account()
    {
        var tokens = await _client.LoginAsync(await factory.CreateUserAsync("not-admin"));

        var error = await (await _client.GetAsync("/api/v1/admin/probe", tokens.AccessToken)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal("Brak uprawnień do tego zasobu", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task The_admin_area_is_open_to_an_administrator()
    {
        var user = await factory.CreateUserAsync("admin");
        await factory.MakeAdminAsync(user);
        var tokens = await _client.LoginAsync(user);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/admin/probe", tokens.AccessToken)).StatusCode);
    }

    /// <summary>The prefix decides, not the endpoint: not even AllowAnonymous opens the admin area.</summary>
    [Fact]
    public async Task An_admin_endpoint_cannot_opt_out_of_the_admin_rule()
    {
        var tokens = await _client.LoginAsync(await factory.CreateUserAsync("not-admin-anon"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/admin/probe-anonymous", accessToken: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/v1/admin/probe-anonymous", tokens.AccessToken)).StatusCode);
    }

    /// <summary>Revoking the role works immediately — the property ADR 0006 pays a query per request for.</summary>
    [Fact]
    public async Task Revoking_the_admin_role_takes_effect_on_the_next_request()
    {
        var user = await factory.CreateUserAsync("demoted");
        await factory.MakeAdminAsync(user);
        var tokens = await _client.LoginAsync(user);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/admin/probe", tokens.AccessToken)).StatusCode);

        await factory.MakeAdminAsync(user, admin: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/v1/admin/probe", tokens.AccessToken)).StatusCode);
    }

    /// <summary>A suspended administrator is suspended first: SUSPENDED replaces every other role.</summary>
    [Fact]
    public async Task A_suspended_administrator_loses_the_admin_area_too()
    {
        var user = await factory.CreateUserAsync("suspended-admin");
        await factory.MakeAdminAsync(user);
        var tokens = await _client.LoginAsync(user);
        await factory.SuspendAsync(user);

        await (await _client.GetAsync("/api/v1/admin/probe", tokens.AccessToken)).ReadErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
    }
}
