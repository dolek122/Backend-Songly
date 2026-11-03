using System.Net;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// The test accounts of the Local environment. Ported from the original
/// <c>LocalDevAccountsSeederTest</c>, which was a unit test with mocks for one reason: its
/// integration tests shared one database, so the seeded administrator would have stayed there for
/// every other test (as a recipient of mail to administrators, for one). Here each test gets a
/// database of its own, so the accounts are tested the way a developer uses them — by logging in.
/// </summary>
public sealed class LocalDevAccountsTests(SonglyApiFactory factory)
{
    private const string AdminEmail = "admin@songly.local";
    private const string UserEmail = "user@songly.local";
    private const string Password = "Songly123!";

    [Fact]
    public async Task Creates_an_administrator_and_a_regular_user_who_can_log_in()
    {
        await using var database = await TemporaryDatabase.CreateAsync(factory);
        using var app = WithLocalAccounts(database);
        var client = app.CreateClient();

        var admin = await (await client.TryLoginAsync(AdminEmail, Password)).ReadTokensAsync();
        var user = await (await client.TryLoginAsync(UserEmail, Password)).ReadTokensAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/admins", admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/admins", user.AccessToken)).StatusCode);

        // Created the way verification creates an account, so they have profiles like any other.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles/me", admin.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles/me", user.AccessToken)).StatusCode);
    }

    /// <summary>A restart must reset neither passwords nor whatever someone has clicked together.</summary>
    [Fact]
    public async Task A_restart_leaves_the_accounts_untouched()
    {
        await using var database = await TemporaryDatabase.CreateAsync(factory);
        const string changed = "Changed-password-1";
        using (var first = WithLocalAccounts(database))
        {
            var client = first.CreateClient();
            var user = await (await client.TryLoginAsync(UserEmail, Password)).ReadTokensAsync();
            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.PutJsonAsync("/api/v1/auth/password", new { currentPassword = Password, newPassword = changed }, user.AccessToken)).StatusCode);
        }

        using var second = WithLocalAccounts(database);
        var restarted = second.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await restarted.TryLoginAsync(UserEmail, changed)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await restarted.TryLoginAsync(UserEmail, Password)).StatusCode);
        Assert.Equal(2L, await database.QuerySingleAsync<long>("SELECT count(*) FROM users"));
    }

    /// <summary>Nothing is created unless the Local environment asks for it.</summary>
    [Fact]
    public async Task Other_environments_get_no_accounts()
    {
        await using var database = await TemporaryDatabase.CreateAsync(factory);
        using var app = factory.WithSettings(database.Settings);
        _ = app.Services;

        Assert.Equal(0L, await database.QuerySingleAsync<long>("SELECT count(*) FROM users"));
    }

    /// <summary>
    /// The password is in the repository: where real secrets are required, the accounts would be a
    /// public key to the administration. A real JWT key is set, so that it is the local accounts —
    /// not the development key — that stop the start.
    /// </summary>
    [Fact]
    public async Task Refuses_to_start_where_real_secrets_are_required()
    {
        await using var database = await TemporaryDatabase.CreateAsync(factory);
        using var app = factory.WithSettings(
        [
            .. database.Settings,
            ("Songly:LocalDevAccounts:Enabled", "true"),
            ("Songly:Security:ProductionSecretsRequired", "true"),
            ("Songly:Security:Jwt:Secret", "Zq8v3Nf0a7LwXy2Pk5Rt9Bc4Hd6Jm1Qs8Vu0Ge3Ti7Ko"),
        ]);

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Equal(0L, await database.QuerySingleAsync<long>("SELECT count(*) FROM users"));
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithLocalAccounts(TemporaryDatabase database) =>
        factory.WithSettings([.. database.Settings, ("Songly:LocalDevAccounts:Enabled", "true")]);
}
