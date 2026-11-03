using System.Net;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// The session cleanup job, against real Postgres: it stands on a predicate over two columns and a
/// batched <c>DELETE</c>, and only the database can say which rows it really removes. Ported from
/// the original <c>SessionCleanupIntegrationTest</c>, together with
/// <c>reuseIsStillDetected_afterSessionCleanupRan</c> from its refresh-token reuse tests.
/// </summary>
/// <remarks>
/// Every test runs the job in an instance of its own and waits for its run at startup. The theft
/// detection test lives here, not with the other reuse tests, so that no two instances running
/// the job ever overlap: test classes run in parallel, tests within a class do not.
/// </remarks>
public sealed class SessionCleanupTests(SonglyApiFactory factory)
{
    // Older than any session another test creates, so with "oldest first" these are the first
    // rows the job reaches.
    private static readonly DateTimeOffset LongAgo = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Removes_expired_sessions_and_keeps_revoked_ones_until_they_expire()
    {
        var user = await factory.CreateUserAsync("session-cleanup");
        var now = DateTimeOffset.UtcNow;
        var longExpired = await InsertSession(user.Id, expiresAt: now.AddDays(-30));
        var longRevoked = await InsertSession(user.Id, expiresAt: now.AddDays(23), revokedAt: now.AddDays(-30));
        var justExpired = await InsertSession(user.Id, expiresAt: now.AddDays(-1));
        var justRevoked = await InsertSession(user.Id, expiresAt: now.AddDays(29), revokedAt: now.AddDays(-1));
        var live = await InsertSession(user.Id, expiresAt: now.AddDays(30));

        await RunCleanupAsync(user.Id);

        Assert.False(await Exists(longExpired));

        // Revoked a month ago but valid for another 23 days: it stays, because this row is the only
        // way a refresh recognises a stolen copy of the token as reuse.
        Assert.True(await Exists(longRevoked));

        // Expired a day ago — inside the 7-day window for investigating "why was I logged out".
        Assert.True(await Exists(justExpired));
        Assert.True(await Exists(justRevoked));

        // Created two months ago, still valid: the predicate looks at expiry, not at the row's age.
        Assert.True(await Exists(live));
    }

    /// <summary>
    /// Two batches of two, so one run deletes four of five rows — the oldest four — and the fifth
    /// waits for the next run. A neglected table drains over several runs instead of in one that
    /// holds its locks for a quarter of an hour.
    /// </summary>
    [Fact]
    public async Task Deletes_in_batches_oldest_first_and_stops_at_the_ceiling_of_one_run()
    {
        var user = await factory.CreateUserAsync("session-cleanup-batches");

        // Newest first: a DELETE without ORDER BY would take rows in the order they lie on disk,
        // which is insertion order — and would then pass this test only by luck.
        for (var i = 4; i >= 0; i--)
        {
            await InsertSession(user.Id, expiresAt: LongAgo.AddMinutes(i));
        }

        using (var app = factory.WithSettings(
            ("Songly:Security:SessionCleanup:Enabled", "true"),
            ("Songly:Security:SessionCleanup:BatchSize", "2"),
            ("Songly:Security:SessionCleanup:MaxBatchesPerRun", "2")))
        {
            _ = app.Services;
            await WaitUntil(async () => await SessionsOf(user.Id) <= 1);
        }

        Assert.Equal(1L, await SessionsOf(user.Id));
        Assert.Equal(1L, await factory.QuerySingleAsync<long>(
            "SELECT count(*) FROM sessions WHERE user_id = @id AND expires_at = @newest",
            ("id", user.Id),
            ("newest", LongAgo.AddMinutes(4))));

        // The leftover is older than anything else, so it would take a slot in another test's run.
        await factory.ExecuteSqlAsync("DELETE FROM sessions WHERE user_id = @id", ("id", user.Id));
    }

    /// <summary>
    /// Original ADR 0049: revoked rows used to disappear a week after revocation. A user coming back
    /// after eight days with the old token then got a plain 401 — and the thief's chain lived on.
    /// </summary>
    [Fact]
    public async Task Theft_is_still_detected_after_the_cleanup_ran()
    {
        var client = factory.CreateClient();
        var user = await factory.CreateUserAsync("session-cleanup-reuse");
        var stolen = (await client.LoginAsync(user)).RefreshToken;
        var rotated = (await (await client.RefreshAsync(stolen)).ReadTokensAsync()).RefreshToken;
        await factory.ExecuteSqlAsync(
            "UPDATE sessions SET revoked_at = now() - interval '8 days' WHERE refresh_token_hash = @hash",
            ("hash", TestTokens.HashOf(stolen)));

        await RunCleanupAsync(user.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(stolen)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync(rotated)).StatusCode);
    }

    /// <summary>A negative retention would move the cutoff into the future and log people out.</summary>
    [Fact]
    public void A_negative_retention_refuses_to_start()
    {
        using var app = factory.WithSettings(("Songly:Security:SessionCleanup:Retention", "-1.00:00:00"));

        Assert.ThrowsAny<Exception>(() => app.CreateClient());
    }

    /// <summary>
    /// Runs the job once, in an instance of its own, with the default settings. A sentinel session
    /// that expired long ago tells when the run is over: it goes in the first batch.
    /// </summary>
    private async Task RunCleanupAsync(Guid userId)
    {
        var sentinel = await InsertSession(userId, expiresAt: LongAgo);
        using var app = factory.WithSettings(("Songly:Security:SessionCleanup:Enabled", "true"));
        _ = app.Services;
        await WaitUntil(async () => !await Exists(sentinel));
    }

    /// <summary>
    /// Inserted with SQL: the entity rightly does not let anyone set an arbitrary revocation time,
    /// and the age of dead rows is exactly what is tested here.
    /// </summary>
    private async Task<Guid> InsertSession(Guid userId, DateTimeOffset expiresAt, DateTimeOffset? revokedAt = null)
    {
        var id = Guid.NewGuid();
        await factory.ExecuteSqlAsync(
            "INSERT INTO sessions (id, user_id, refresh_token_hash, created_at, expires_at, revoked, revoked_at) " +
            "VALUES (@id, @user, @hash, now() - interval '60 days', @expires, @revoked, @revokedAt)",
            ("id", id),
            ("user", userId),
            ("hash", $"hash-{id}"),
            ("expires", expiresAt),
            ("revoked", revokedAt is not null),
            ("revokedAt", revokedAt));
        return id;
    }

    private async Task<bool> Exists(Guid sessionId) =>
        await factory.QuerySingleAsync<long>("SELECT count(*) FROM sessions WHERE id = @id", ("id", sessionId)) == 1;

    private Task<long> SessionsOf(Guid userId) =>
        factory.QuerySingleAsync<long>("SELECT count(*) FROM sessions WHERE user_id = @id", ("id", userId));

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The cleanup job did not run within 10 seconds");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
