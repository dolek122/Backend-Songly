namespace Songly.IntegrationTests;

public sealed record LoggedInUser(TestUser User, Tokens Tokens)
{
    public Guid Id => User.Id;

    public string Token => Tokens.AccessToken;
}

public static class TestAdmins
{
    /// <summary>
    /// An administrator. The first administrator is created in the database by design (only an
    /// administrator can grant the role), so the test does the same.
    /// </summary>
    public static async Task<LoggedInUser> CreateAdminAsync(this SonglyApiFactory factory, HttpClient client, string prefix = "admin")
    {
        var user = await factory.CreateUserAsync(prefix);
        await factory.MakeAdminAsync(user);
        return new LoggedInUser(user, await client.LoginAsync(user));
    }

    public static async Task<LoggedInUser> CreateLoggedInUserAsync(this SonglyApiFactory factory, HttpClient client, string prefix = "user")
    {
        var user = await factory.CreateUserAsync(prefix);
        return new LoggedInUser(user, await client.LoginAsync(user));
    }
}
