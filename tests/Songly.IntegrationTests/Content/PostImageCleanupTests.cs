using System.Net;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// The cleanup of abandoned uploads (original ADR 0048), run by its job in an instance of its own.
/// Ported from the original <c>PostImageIntegrationTest</c> and <c>PostImageUploadsTest</c>.
/// </summary>
/// <remarks>
/// The age of an upload is set with SQL — it is the subject of the test, and the entity rightly does
/// not let anyone choose it. All runs of the job live in this class, so no two overlap: test classes
/// run in parallel, tests within a class do not. Each run is awaited through a sentinel: the most
/// recently abandoned row (just past the cutoff), so the last one the run reaches.
/// </remarks>
public sealed class PostImageCleanupTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly PostImageClient _images = new(factory.CreateClient());

    [Fact]
    public async Task An_abandoned_upload_is_removed_from_storage_and_can_no_longer_be_attached()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-abandoned");
        var (key, url) = await _images.UploadedAsync(user);
        await AgeAsync(key, TimeSpan.FromDays(2));

        await RunCleanupAsync(user.Id);

        Assert.NotEqual(HttpStatusCode.OK, await PostImageClient.StatusOfAsync(url));

        // The only way an honest client gets here, so the message says what to do instead of
        // accusing anyone of using someone else's key.
        var error = await (await _images.PublishAsync(user, key, "za późno")).ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_POST_IMAGE");
        Assert.Equal("Zdjęcie wygasło albo zostało już użyte. Wgraj je ponownie.", error.GetProperty("message").GetString());
    }

    /// <summary>The cleanup touches neither a fresh upload nor an image already in a post — even one uploaded long ago.</summary>
    [Fact]
    public async Task The_cleanup_leaves_recent_uploads_and_published_images_alone()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-survivor");
        var recent = await _images.UploadedAsync(user);
        var published = await _images.UploadedAsync(user);
        var (_, publishedUrl) = await _images.PublishedAsync(user, published.Key);
        await AgeAsync(published.Key, TimeSpan.FromDays(2));

        await RunCleanupAsync(user.Id);

        Assert.Equal(HttpStatusCode.OK, await PostImageClient.StatusOfAsync(publishedUrl));
        Assert.Equal(HttpStatusCode.OK, await PostImageClient.StatusOfAsync(recent.Url));
        await _images.PublishedAsync(user, recent.Key);
    }

    /// <summary>
    /// The first storage failure stops the run. The first row is already gone, so its file stays
    /// orphaned — but only one. Without stopping, a storage outage deleted every pending row and left
    /// every file behind. The storage here is an address nothing listens on, so every delete fails.
    /// </summary>
    [Fact]
    public async Task A_storage_failure_stops_the_run_before_it_touches_further_rows()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-outage");
        var first = $"posts/{user.Id}/first.png";
        var second = $"posts/{user.Id}/second.png";
        await InsertUploadAsync(first, user.Id, age: TimeSpan.FromDays(3));
        await InsertUploadAsync(second, user.Id, age: TimeSpan.FromDays(2));

        using (var app = factory.WithSettings(
            ("Songly:Content:PostImage:CleanupEnabled", "true"),
            ("Songly:Storage:Endpoint", "http://127.0.0.1:1")))
        {
            _ = app.Services;
            await WaitUntilAsync(async () => !await ExistsAsync(first), "the run never reached the first row");

            // The run either went on to the second row at once, or it stopped. A second is plenty
            // for a delete that has nothing to wait for.
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.True(await ExistsAsync(second));
        }

        await factory.ExecuteSqlAsync("DELETE FROM post_image_uploads WHERE owner_id = @id", ("id", user.Id));
    }

    /// <summary>Runs the job once, in an instance of its own, and waits until it has passed the sentinel.</summary>
    private async Task RunCleanupAsync(Guid ownerId)
    {
        var sentinel = $"posts/{ownerId}/sentinel-{Guid.NewGuid():N}.png";
        await InsertUploadAsync(sentinel, ownerId, age: TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        using var app = factory.WithSettings(("Songly:Content:PostImage:CleanupEnabled", "true"));
        _ = app.Services;
        await WaitUntilAsync(async () => !await ExistsAsync(sentinel), "the cleanup job did not run");
    }

    private Task AgeAsync(string key, TimeSpan age) =>
        factory.ExecuteSqlAsync("UPDATE post_image_uploads SET created_at = now() - @age WHERE image_key = @key", ("age", age), ("key", key));

    /// <summary>A row without a file — the storage answers a delete of a missing key with success.</summary>
    private Task InsertUploadAsync(string key, Guid ownerId, TimeSpan age) =>
        factory.ExecuteSqlAsync(
            "INSERT INTO post_image_uploads (image_key, owner_id, created_at) VALUES (@key, @owner, now() - @age)",
            ("key", key),
            ("owner", ownerId),
            ("age", age));

    private async Task<bool> ExistsAsync(string key) =>
        await factory.QuerySingleAsync<long>("SELECT count(*) FROM post_image_uploads WHERE image_key = @key", ("key", key)) == 1;

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
