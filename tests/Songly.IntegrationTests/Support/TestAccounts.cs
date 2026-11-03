namespace Songly.IntegrationTests;

public sealed record TestUser(Guid Id, string Email, string Password);

public static class TestAccounts
{
    public const string DefaultPassword = "password123";

    /// <summary>An address no other test uses — the database is shared by the whole suite.</summary>
    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@songly.io";

    /// <summary>
    /// Creates an account the way a user does: registration, the link from the e-mail, and a
    /// display name — so the account has everything other modules set up for a new one (a
    /// profile, to begin with).
    /// </summary>
    /// <remarks>
    /// The display name is the part of the address before the "@", set explicitly the way a user
    /// would set it: tests search for and compare names, so they need predictable ones. The default
    /// name is random (original ADR 0046) and is tested on its own, without this helper. The
    /// recorded verification e-mail is forgotten, so the inbox of a new account starts empty.
    /// </remarks>
    public static async Task<TestUser> CreateUserAsync(this SonglyApiFactory factory, string prefix = "user", string password = DefaultPassword)
    {
        var email = UniqueEmail(prefix);
        var client = factory.CreateClient();
        var tokens = await client.RegisterAndVerifyAsync(factory.Mails, email, password);
        var response = await client.PutJsonAsync("/api/v1/profiles/me", new { displayName = email[..email.IndexOf('@', StringComparison.Ordinal)] }, tokens.AccessToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        // The verification link was setup: a test counting what reached this inbox counts what it caused.
        factory.Mails.ClearFor(email);
        return new TestUser(TestTokens.SubjectOf(tokens.AccessToken), email, password);
    }

    public static Task MakeAdminAsync(this SonglyApiFactory factory, TestUser user, bool admin = true) =>
        factory.ExecuteSqlAsync("UPDATE users SET is_admin = @admin WHERE id = @id", ("admin", admin), ("id", user.Id));

    public static Task SuspendAsync(this SonglyApiFactory factory, TestUser user, DateTimeOffset? expiresAt = null) =>
        factory.ExecuteSqlAsync(
            "UPDATE users SET suspended_at = now(), suspension_reason = 'test', suspension_expires_at = @expires WHERE id = @id",
            ("expires", expiresAt),
            ("id", user.Id));
}
