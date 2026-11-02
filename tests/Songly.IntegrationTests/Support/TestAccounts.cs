namespace Songly.IntegrationTests;

public sealed record TestUser(Guid Id, string Email, string Password);

public static class TestAccounts
{
    public const string DefaultPassword = "password123";

    /// <summary>An address no other test uses — the database is shared by the whole suite.</summary>
    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@songly.io";

    /// <summary>
    /// Creates an account directly in the database.
    /// </summary>
    /// <remarks>
    /// Faster than registering and verifying, and enough while no other module reacts to a new
    /// account. Once the profile module is ported (accounts get a profile when they are created),
    /// tests that need a complete account go through registration instead. The hash uses bcrypt cost
    /// 4 instead of the application's 10: verification time depends on the cost stored in the
    /// hash, and the suite creates hundreds of accounts.
    /// </remarks>
    public static async Task<TestUser> CreateUserAsync(this SonglyApiFactory factory, string prefix = "user", string password = DefaultPassword)
    {
        var user = new TestUser(Guid.NewGuid(), UniqueEmail(prefix), password);
        await factory.ExecuteSqlAsync(
            "INSERT INTO users (id, email, password_hash, created_at) VALUES (@id, @email, @hash, now())",
            ("id", user.Id),
            ("email", user.Email),
            ("hash", BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4)));
        return user;
    }

    public static Task MakeAdminAsync(this SonglyApiFactory factory, TestUser user, bool admin = true) =>
        factory.ExecuteSqlAsync("UPDATE users SET is_admin = @admin WHERE id = @id", ("admin", admin), ("id", user.Id));

    public static Task SuspendAsync(this SonglyApiFactory factory, TestUser user, DateTimeOffset? expiresAt = null) =>
        factory.ExecuteSqlAsync(
            "UPDATE users SET suspended_at = now(), suspension_reason = 'test', suspension_expires_at = @expires WHERE id = @id",
            ("expires", expiresAt),
            ("id", user.Id));
}
