using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Platform-wide account suspension (original ADR 0022). Ported from the original
/// <c>AccountSuspensionIntegrationTest</c>.
/// </summary>
/// <remarks>
/// What matters most: a suspension cuts off <b>everything</b> — enforcement lives in the roles, not
/// in checks scattered over modules — and still leaves open exactly what must stay open. The
/// e-mail to the suspended user (term, never the reason) belongs to the notification module and is
/// tested with it; account deletion while suspended, with account deletion.
/// </remarks>
public sealed class AccountSuspensionTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> Suspend(LoggedInUser admin, Guid target, object body) =>
        _client.PutJsonAsync($"/api/v1/admin/users/{target}/suspension", body, admin.Token);

    private async Task<JsonElement> OwnSuspension(string accessToken)
    {
        var response = await _client.GetAsync("/api/v1/auth/me/suspension", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Everything is closed except the account's own status (and, through the appeal tests, the
    /// appeal). The very same token worked a moment ago — the role is read live.
    /// </summary>
    [Fact]
    public async Task A_suspended_account_loses_everything_except_its_own_status()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var troll = await factory.CreateLoggedInUserAsync(_client, "troll");

        Assert.Equal(HttpStatusCode.NoContent, (await Suspend(admin, troll.Id, new { reason = "nękanie innych" })).StatusCode);

        await (await _client.GetAsync("/probe-secured/whoami", troll.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");
        await (await _client.PutJsonAsync("/api/v1/auth/password", new { currentPassword = troll.User.Password, newPassword = "brand-new-password" }, troll.Token))
            .ReadErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");

        var status = await OwnSuspension(troll.Token);
        Assert.True(status.GetProperty("suspended").GetBoolean());
        Assert.Equal("nękanie innych", status.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("expiresAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("appealStatus").ValueKind);
    }

    /// <summary>
    /// Logging in still works on purpose: without a token the account could not learn the reason,
    /// appeal, or delete itself. What the token allows is decided by roles.
    /// </summary>
    [Fact]
    public async Task A_suspended_account_can_still_log_in_and_read_its_status()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var troll = await factory.CreateUserAsync("troll-login");
        await Suspend(admin, troll.Id, new { reason = "spam", durationDays = 7 });

        var tokens = await _client.LoginAsync(troll);

        var status = await OwnSuspension(tokens.AccessToken);
        Assert.True(status.GetProperty("suspended").GetBoolean());
        var expiresAt = DateTimeOffset.Parse(status.GetProperty("expiresAt").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(5));
    }

    /// <summary>Expiry is lazy: nothing wakes up when the term ends, the account simply works again.</summary>
    [Fact]
    public async Task An_expired_suspension_restores_the_account_by_itself()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "expired");
        await Suspend(admin, user.Id, new { reason = "na chwilę", durationDays = 1 });
        await (await _client.GetAsync("/probe-secured/whoami", user.Token)).ReadErrorAsync(HttpStatusCode.Forbidden, "ACCOUNT_SUSPENDED");

        await factory.ExecuteSqlAsync("UPDATE users SET suspension_expires_at = now() - interval '1 minute' WHERE id = @id", ("id", user.Id));

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/probe-secured/whoami", user.Token)).StatusCode);
        Assert.False((await OwnSuspension(user.Token)).GetProperty("suspended").GetBoolean());
        Assert.DoesNotContain(user.Id.ToString(), await SuspensionsListed(admin), StringComparison.Ordinal);
    }

    /// <summary>The list shows active suspensions only — an expired one would suggest a sanction that does not exist.</summary>
    [Fact]
    public async Task Lifting_restores_the_account_and_the_list_shows_only_active_suspensions()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "lifted");
        await Suspend(admin, user.Id, new { reason = "pomyłka" });
        Assert.Contains(user.Id.ToString(), await SuspensionsListed(admin), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{user.Id}/suspension", admin.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/probe-secured/whoami", user.Token)).StatusCode);
        Assert.DoesNotContain(user.Id.ToString(), await SuspensionsListed(admin), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{user.Id}/suspension", admin.Token)).StatusCode);
    }

    /// <summary>The list carries the moderation state, not contact data.</summary>
    [Fact]
    public async Task The_list_of_suspensions_has_no_email_addresses()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateUserAsync("listed");
        await Suspend(admin, user.Id, new { reason = "spam" });

        var listed = await SuspensionsListed(admin);

        Assert.Contains(user.Id.ToString(), listed, StringComparison.Ordinal);
        Assert.DoesNotContain("@", listed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_admin_cannot_suspend_themselves_or_another_admin()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var otherAdmin = await factory.CreateAdminAsync(_client, "other-admin");

        await (await Suspend(admin, admin.Id, new { reason = "test" })).ReadErrorAsync(HttpStatusCode.Forbidden, "CANNOT_SUSPEND_SELF");
        await (await Suspend(admin, otherAdmin.Id, new { reason = "test" })).ReadErrorAsync(HttpStatusCode.Forbidden, "CANNOT_SUSPEND_ADMIN");
    }

    /// <summary>A suspension without a reason could not be reconstructed — and the suspended user will read it.</summary>
    [Fact]
    public async Task The_reason_is_required()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateUserAsync("no-reason");

        await (await Suspend(admin, user.Id, new { reason = "  " })).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await (await Suspend(admin, user.Id, new { reason = new string('x', 501) })).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    /// <summary>Hundreds of millions of days overflow Postgres's timestamp range — a 400, not a 500 (original ADR 0049).</summary>
    [Theory]
    [InlineData(2_000_000_000)]
    [InlineData(36_501)]
    [InlineData(0)]
    public async Task A_term_outside_one_day_to_a_hundred_years_is_rejected(int durationDays)
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateUserAsync("absurd-term");

        await (await Suspend(admin, user.Id, new { reason = "nękanie innych", durationDays })).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Suspending_an_unknown_account_is_404()
    {
        var admin = await factory.CreateAdminAsync(_client);

        await (await Suspend(admin, Guid.NewGuid(), new { reason = "spam" })).ReadErrorAsync(HttpStatusCode.NotFound, "USER_NOT_FOUND");
    }

    /// <summary>Re-deciding an ongoing suspension replaces its reason and term.</summary>
    [Fact]
    public async Task Suspending_again_replaces_the_reason_and_the_term()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "re-decided");
        await Suspend(admin, user.Id, new { reason = "literówka", durationDays = 3 });

        await Suspend(admin, user.Id, new { reason = "spam" });

        var status = await OwnSuspension(user.Token);
        Assert.Equal("spam", status.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("expiresAt").ValueKind);
    }

    /// <summary>An active account is not "not found": the client asks after every ACCOUNT_SUSPENDED.</summary>
    [Fact]
    public async Task An_active_account_reads_suspended_false_with_empty_fields()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "active");

        var status = await OwnSuspension(user.Token);

        Assert.False(status.GetProperty("suspended").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("suspendedAt").ValueKind);
    }

    private async Task<string> SuspensionsListed(LoggedInUser admin)
    {
        var response = await _client.GetAsync("/api/v1/admin/suspensions", admin.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
