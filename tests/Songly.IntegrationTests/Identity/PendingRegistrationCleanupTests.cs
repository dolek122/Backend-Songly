namespace Songly.IntegrationTests.Identity;

/// <summary>Ported from the original <c>PendingRegistrationCleanupIntegrationTest</c>.</summary>
public sealed class PendingRegistrationCleanupTests(SonglyApiFactory factory)
{
    /// <summary>
    /// The verification mail promises that an unconfirmed registration "expires by itself". That
    /// has to be true of the data — the address and the password hash — not only of the link.
    /// </summary>
    [Fact]
    public async Task Removes_expired_registrations_and_keeps_the_ones_still_waiting()
    {
        var expired = TestAccounts.UniqueEmail("cleanup-expired");
        var waiting = TestAccounts.UniqueEmail("cleanup-waiting");
        await Insert(expired, "now() - interval '1 minute'");
        await Insert(waiting, "now() + interval '1 day'");

        using var app = factory.WithSettings(
            ("Songly:Security:Registration:CleanupEnabled", "true"),
            ("Songly:Security:Registration:CleanupInterval", "00:00:00.200"));
        _ = app.Services;

        await WaitUntil(async () => await Count(expired) == 0);
        Assert.Equal(1L, await Count(waiting));
    }

    private Task Insert(string email, string expiresAt) => factory.ExecuteSqlAsync(
        $"INSERT INTO pending_registrations (id, email, password_hash, token_hash, created_at, expires_at) VALUES (gen_random_uuid(), @email, 'irrelevant', @hash, now() - interval '2 days', {expiresAt})",
        ("email", email),
        ("hash", Guid.NewGuid().ToString()));

    private Task<long> Count(string email) =>
        factory.QuerySingleAsync<long>("SELECT count(*) FROM pending_registrations WHERE email = @email", ("email", email));

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The cleanup job did not run within 10 seconds");
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
