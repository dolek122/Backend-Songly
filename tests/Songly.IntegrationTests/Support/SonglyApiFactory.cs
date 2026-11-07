using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Songly.Common.Events;
using Songly.Common.Modules;
using Songly.Common.Persistence;
using Songly.Identity.Contracts;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Songly.IntegrationTests.SonglyApiFactory))]

namespace Songly.IntegrationTests;

/// <summary>
/// The whole application against a real Postgres and Redis in containers — never an in-memory
/// substitute, which would lie about how they behave. One instance is shared by the whole test
/// assembly, so every test creates its own accounts and never assumes an empty database.
/// </summary>
public sealed class SonglyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same images as docker-compose and the original backend's tests.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    // The Chainguard build of MinIO, as in docker-compose and the original's tests: MinIO no longer
    // publishes its own images anonymously.
    private readonly MinioContainer _minio = new MinioBuilder("cgr.dev/chainguard/minio:latest").Build();

    public const string StorageBucket = "songly-media";

    public string ConnectionString => _postgres.GetConnectionString();

    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>Where uploaded files are served from — fetched by tests anonymously, as clients do.</summary>
    public string StoragePublicBaseUrl => $"{StorageEndpoint}/{StorageBucket}";

    // Testcontainers ends the address with a slash; the key would then start with "//".
    private string StorageEndpoint => _minio.GetConnectionString().TrimEnd('/');

    /// <summary>Every e-mail this application instance sent.</summary>
    public RecordingMailSender Mails => this.MailsOf();

    public RecordingPushSender Pushes => this.PushesOf();

    /// <summary>
    /// Starts the application right away, not lazily at the first client: the migrations run at
    /// startup, and a test that seeds data with SQL before making a request would otherwise find
    /// no tables — but only when it happens to run first.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _minio.StartAsync());
        await CreatePublicBucketAsync();
        _ = Services;
    }

    /// <summary>
    /// What <c>minio-init</c> does in docker-compose: a bucket anyone can read from. Avatars are
    /// public by design, and clients load them straight from the storage.
    /// </summary>
    private async Task CreatePublicBucketAsync()
    {
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(_minio.GetAccessKey(), _minio.GetSecretKey()),
            new AmazonS3Config { ServiceURL = StorageEndpoint, ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        await client.PutBucketAsync(StorageBucket);
        await client.PutBucketPolicyAsync(new PutBucketPolicyRequest
        {
            BucketName = StorageBucket,
            Policy = $$"""{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"AWS":["*"]},"Action":["s3:GetObject"],"Resource":["arn:aws:s3:::{{StorageBucket}}/*"]}]}""",
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await _minio.DisposeAsync();
    }

    /// <summary>
    /// A second instance of the application on the same database and Redis, with different
    /// settings — what a test that needs, say, no resend interval runs against. The schema is
    /// already migrated by this instance.
    /// </summary>
    public WebApplicationFactory<Program> WithSettings(params (string Key, string? Value)[] settings) =>
        WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:MigrateOnStartup", "false");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    /// <summary>
    /// Changes data behind the application's back — for state the API cannot (yet) produce, or
    /// whose age is the subject of the test.
    /// </summary>
    public async Task<int> ExecuteSqlAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = Command(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T> QuerySingleAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = Command(connection, sql, parameters);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Songly", ConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("Songly:Storage:Endpoint", StorageEndpoint);
        builder.UseSetting("Songly:Storage:AccessKey", _minio.GetAccessKey());
        builder.UseSetting("Songly:Storage:SecretKey", _minio.GetSecretKey());
        builder.UseSetting("Songly:Storage:PublicBaseUrl", StoragePublicBaseUrl);

        // Warnings and up only: at Information every SQL statement of hundreds of parallel tests lands on
        // the console, and the one failing test's message drowns in it — CI's log tail shows only noise.
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");

        // Off here: they run at startup and would race tests that insert expired rows on purpose.
        // The tests of the jobs themselves turn them on in an instance of their own.
        builder.UseSetting("Songly:Security:Registration:CleanupEnabled", "false");
        builder.UseSetting("Songly:Security:SessionCleanup:Enabled", "false");
        builder.UseSetting("Songly:Content:PostImage:CleanupEnabled", "false");
        builder.UseSetting("Songly:NowPlaying:Poll:Enabled", "false");
        builder.UseSetting("Songly:NowPlaying:Stream:HeartbeatEnabled", "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IModule, ProbeModule>();
            services.AddModuleDbContext<ProbeDbContext>();
            services.AddScoped<IDomainEventHandler<UserAccountDeletionRequested>, ProbeDeletionHandler>();
            services.AddScoped<IDomainEventHandler<UserRegistered>, ProbeRegistrationHandler>();
            RecordingMailSender.Install(services);
            RecordingPushSender.Install(services);
        });
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
