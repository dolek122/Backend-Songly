using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Songly.Profile.Contracts;
using StackExchange.Redis;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// The genre popularity snapshot in Redis (original ADR 0032). Ported from the original
/// <c>GenrePopularityCacheIntegrationTest</c> and <c>GenrePopularityCacheTest</c>.
/// </summary>
/// <remarks>
/// <para>
/// The numbers are changed <b>behind</b> the application, with SQL. It is the only way: every path
/// of the application that touches <c>user_genres</c> evicts the snapshot itself, so the API cannot
/// produce the mismatch these tests look for.
/// </para>
/// <para>
/// Every test runs an instance of its own on a <see cref="TemporaryDatabase"/> and a Redis database
/// of its own. The snapshot is one key for the whole installation: in the shared instance, any other
/// test changing its genres would evict it in the middle of these.
/// </para>
/// </remarks>
public sealed class GenrePopularityCacheTests(SonglyApiFactory factory)
{
    // Metal, from the V2 seed.
    private static readonly Guid Metal = Guid.Parse("a5a9f290-1111-4a2e-8b1a-000000000008");

    /// <summary>
    /// ADR 0032 both ways: while nobody changes their genres, the numbers come from the cache (so a
    /// row added behind the application is invisible), and the first change through the API evicts
    /// the snapshot and the numbers catch up with the database.
    /// </summary>
    [Fact]
    public async Task Popularity_is_cached_until_favourite_genres_change()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var owner = await isolated.RegisterAsync("genrecache-owner");
        var bystander = await isolated.RegisterAsync("genrecache-bystander");
        await isolated.SetMetalAsync(owner);
        Assert.Equal(1, await isolated.MetalCountAsync());

        await isolated.AddMetalBehindTheApplicationAsync(bystander);
        Assert.Equal(1, await isolated.MetalCountAsync());

        // The same genres again — the numbers do not change, but the snapshot goes.
        await isolated.SetMetalAsync(owner);
        Assert.Equal(2, await isolated.MetalCountAsync());
    }

    /// <summary>
    /// The weight's denominator (original ADR 0029) is in the same entry as the counts, so it is
    /// evicted with them — otherwise <c>ln(1 + N/n)</c> would be computed from two different moments.
    /// </summary>
    [Fact]
    public async Task The_total_comes_from_the_same_snapshot()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var owner = await isolated.RegisterAsync("genrecache-total");
        var bystander = await isolated.RegisterAsync("genrecache-total-bystander");
        await isolated.SetMetalAsync(owner);
        Assert.Equal(1, await isolated.Directory(d => d.UsersWithAnyGenreCountAsync(TestContext.Current.CancellationToken)));

        await isolated.AddMetalBehindTheApplicationAsync(bystander);
        Assert.Equal(1, await isolated.Directory(d => d.UsersWithAnyGenreCountAsync(TestContext.Current.CancellationToken)));

        await isolated.SetMetalAsync(owner);
        Assert.Equal(2, await isolated.Directory(d => d.UsersWithAnyGenreCountAsync(TestContext.Current.CancellationToken)));
    }

    /// <summary>A deleted account takes its genres with it — the second path that changes popularity.</summary>
    [Fact]
    public async Task Deleting_an_account_evicts_the_snapshot()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var staying = await isolated.RegisterAsync("genrecache-staying");
        var leaving = await isolated.RegisterAsync("genrecache-leaving");
        await isolated.SetMetalAsync(staying);
        await isolated.SetMetalAsync(leaving);
        Assert.Equal(2, await isolated.MetalCountAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await isolated.DeleteAccountAsync(leaving)).StatusCode);

        Assert.Equal(1, await isolated.MetalCountAsync());
    }

    /// <summary>A damaged entry reads as a missing one: counted from the database and overwritten.</summary>
    [Fact]
    public async Task A_damaged_snapshot_is_counted_again_and_replaced()
    {
        await using var isolated = await Isolated.StartAsync(factory);
        var owner = await isolated.RegisterAsync("genrecache-garbage");
        await isolated.SetMetalAsync(owner);
        await isolated.Redis.StringSetAsync("genre-popularity", "{to nie jest JSON");

        Assert.Equal(1, await isolated.MetalCountAsync());
        Assert.StartsWith("{\"Counts\"", (await isolated.Redis.StringGetAsync("genre-popularity")).ToString(), StringComparison.Ordinal);
    }

    /// <summary>The cache is an optimisation, not a condition of correctness: without Redis, the numbers come from the database.</summary>
    [Fact]
    public async Task Without_redis_the_numbers_come_from_the_database()
    {
        await using var isolated = await Isolated.StartAsync(factory, redis: "127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500,asyncTimeout=500");
        var owner = await isolated.RegisterAsync("genrecache-no-redis");
        await isolated.SetMetalAsync(owner);
        Assert.Equal(1, await isolated.MetalCountAsync());

        await isolated.AddMetalBehindTheApplicationAsync(await isolated.RegisterAsync("genrecache-no-redis-bystander"));

        Assert.Equal(2, await isolated.MetalCountAsync());
    }

    /// <summary>An application instance with a database and a Redis database of its own.</summary>
    private sealed class Isolated : IAsyncDisposable
    {
        private static int _nextRedisDatabase = 1;

        private readonly TemporaryDatabase _database;
        private readonly WebApplicationFactory<Program> _app;
        private readonly HttpClient _client;

        private Isolated(TemporaryDatabase database, WebApplicationFactory<Program> app)
        {
            _database = database;
            _app = app;
            _client = app.CreateClient();
        }

        public IDatabase Redis => _app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

        public static async Task<Isolated> StartAsync(SonglyApiFactory factory, string? redis = null)
        {
            var database = await TemporaryDatabase.CreateAsync(factory);
            redis ??= $"{factory.RedisConnectionString},defaultDatabase={Interlocked.Increment(ref _nextRedisDatabase) % 15 + 1}";
            var app = factory.WithSettings([.. database.Settings, ("ConnectionStrings:Redis", redis)]);
            var isolated = new Isolated(database, app);
            if (redis.StartsWith(factory.RedisConnectionString, StringComparison.Ordinal))
            {
                await isolated.Redis.KeyDeleteAsync("genre-popularity");
            }

            return isolated;
        }

        public async Task<LoggedInUser> RegisterAsync(string prefix)
        {
            var email = TestAccounts.UniqueEmail(prefix);
            var tokens = await _client.RegisterAndVerifyAsync(_app.MailsOf(), email);
            return new LoggedInUser(new TestUser(TestTokens.SubjectOf(tokens.AccessToken), email, TestAccounts.DefaultPassword), tokens);
        }

        /// <summary>Through the API — the path that evicts the snapshot.</summary>
        public async Task SetMetalAsync(LoggedInUser user) =>
            Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { Metal } }, user.Token)).StatusCode);

        /// <summary>Behind the application — the database knows, the cache does not.</summary>
        public Task AddMetalBehindTheApplicationAsync(LoggedInUser user) =>
            _database.ExecuteSqlAsync("INSERT INTO user_genres (user_id, genre_id, created_at) VALUES (@user, @genre, now())", ("user", user.Id), ("genre", Metal));

        public async Task<long> MetalCountAsync() =>
            (await Directory(d => d.GenreUserCountsAsync([Metal], TestContext.Current.CancellationToken))).GetValueOrDefault(Metal);

        public async Task<T> Directory<T>(Func<IProfileDirectory, Task<T>> query)
        {
            using var scope = _app.Services.CreateScope();
            return await query(scope.ServiceProvider.GetRequiredService<IProfileDirectory>());
        }

        public Task<HttpResponseMessage> DeleteAccountAsync(LoggedInUser user)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/me") { Content = System.Net.Http.Json.JsonContent.Create(new { password = user.User.Password }) };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);
            return _client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
            await _database.DisposeAsync();
        }
    }
}
