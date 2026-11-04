using System.Net;
using System.Text;
using Songly.Common.Media;
using Songly.IntegrationTests.Community;
using Songly.Tests.Shared;

namespace Songly.IntegrationTests.Content;

/// <summary>
/// Post images: upload, attaching through the register of uploads (original ADR 0048), metadata
/// stripping (ADR 0047) and deleting the file on every path that orphans it. Ported from the
/// original <c>PostImageIntegrationTest</c>; the cleanup job's scenarios are in
/// <see cref="PostImageCleanupTests"/>.
/// </summary>
public sealed class PostImageTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly PostImageClient _images = new(factory.CreateClient());
    private readonly ContentTestClient _content = new(factory.CreateClient());

    [Fact]
    public async Task An_uploaded_image_is_attached_to_the_post_and_publicly_fetchable()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-happy");
        var (key, url) = await _images.UploadedAsync(user);
        Assert.StartsWith($"posts/{user.Id}/", key, StringComparison.Ordinal);
        Assert.Equal($"{factory.StoragePublicBaseUrl}/{key}", url);

        var (postId, imageUrl) = await _images.PublishedAsync(user, key);

        Assert.Equal(url, imageUrl);
        Assert.Equal(TestImages.Png(), await (await PostImageClient.FetchAsync(imageUrl)).Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(imageUrl, (await _content.PostAsync(user, postId)).GetProperty("imageUrl").GetString());
    }

    /// <summary>A post's image is public, and the camera's EXIF says where it was taken (original ADR 0047). The stored file is checked, not the response.</summary>
    [Fact]
    public async Task An_uploaded_image_is_stored_without_camera_metadata()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-exif");

        var (_, url) = await _images.UploadedAsync(user, TestImages.JpegWithExif(6));

        var stored = await (await PostImageClient.FetchAsync(url)).Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.False(TestImages.Contains(stored, TestImages.Secret));
        Assert.Equal(SanitizedImage.From(TestImages.JpegWithExif(6)).Content, stored);
        Assert.EndsWith(".jpg", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_rejected()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-notimage");

        await (await _images.UploadAsync(user, Encoding.ASCII.GetBytes("zwykły tekst"), "plik.png", "image/png")).ReadErrorAsync(HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_IMAGE");
        await (await _images.UploadAsync(user, [], "pusty.png", "image/png")).ReadErrorAsync(HttpStatusCode.BadRequest, "EMPTY_UPLOAD");
    }

    /// <summary>
    /// The point of the register: someone else's key does not attach to one's own post. Otherwise
    /// giving someone's key would show their image under one's name — and deleting that post would
    /// delete the owner's file.
    /// </summary>
    [Fact]
    public async Task Another_users_image_key_is_rejected()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "post-image-owner");
        var thief = await factory.CreateLoggedInUserAsync(_client, "post-image-thief");
        var (key, _) = await _images.UploadedAsync(owner);

        await (await _images.PublishAsync(thief, key, "nie moje")).ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_POST_IMAGE");

        // ...and the owner's upload is untouched by the attempt.
        await _images.PublishedAsync(owner, key);
    }

    /// <summary>A made-up key, and one's own prefix with <c>..</c> after it (an HTTP client resolves such segments before sending), have no row.</summary>
    [Fact]
    public async Task Made_up_keys_and_path_traversal_are_rejected()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "post-image-trav-owner");
        var thief = await factory.CreateLoggedInUserAsync(_client, "post-image-trav-thief");
        var (foreignKey, _) = await _images.UploadedAsync(owner);

        await (await _images.PublishAsync(thief, "posts/../../etc/passwd")).ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_POST_IMAGE");
        await (await _images.PublishAsync(thief, $"posts/{thief.Id}/../../{foreignKey}")).ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_POST_IMAGE");
    }

    /// <summary>One file, one post. The same key used to attach twice, and deleting one post deleted the other's image.</summary>
    [Fact]
    public async Task The_same_upload_cannot_be_attached_to_two_posts()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-twice");
        var (key, _) = await _images.UploadedAsync(user);
        await _images.PublishedAsync(user, key);

        var second = await _images.PublishAsync(user, key, "Drugi");

        var error = await second.ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_POST_IMAGE");
        Assert.Equal("Zdjęcie wygasło albo zostało już użyte. Wgraj je ponownie.", error.GetProperty("message").GetString());
    }

    /// <summary>
    /// Above the limit (10 by default) a new upload evicts the oldest instead of failing — browsing
    /// the gallery in the app must not block anyone for a day.
    /// </summary>
    [Fact]
    public async Task An_upload_beyond_the_pending_limit_evicts_the_oldest()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-evict");
        var oldest = await _images.UploadedAsync(user);
        var secondOldest = await _images.UploadedAsync(user);
        for (var i = 0; i < 9; i++)
        {
            await _images.UploadedAsync(user);
        }

        Assert.NotEqual(HttpStatusCode.OK, await PostImageClient.StatusOfAsync(oldest.Url));
        await (await _images.PublishAsync(user, oldest.Key, "wyparte")).ReadErrorAsync(HttpStatusCode.BadRequest, "UNKNOWN_POST_IMAGE");
        Assert.Equal(HttpStatusCode.OK, await PostImageClient.StatusOfAsync(secondOldest.Url));
        await _images.PublishedAsync(user, secondOldest.Key);
    }

    /// <summary>
    /// Deleting a post deletes its image. Without that, every deleted post would leave a file nothing
    /// points to and no row in the database leads to — impossible to find even by hand.
    /// </summary>
    [Fact]
    public async Task Deleting_a_post_removes_its_image()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-delete");
        var (postId, imageUrl) = await _images.PublishedAsync(user, (await _images.UploadedAsync(user)).Key);
        Assert.Equal(HttpStatusCode.OK, await PostImageClient.StatusOfAsync(imageUrl));

        Assert.Equal(HttpStatusCode.NoContent, (await _content.DeletePostAsync(user, postId)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await PostImageClient.StatusOfAsync(imageUrl));
    }

    /// <summary>
    /// Account deletion cleans the files too — of the posts and of the pending uploads. The posts go
    /// in bulk, so the keys have to be read first; afterwards there is nowhere to read them from.
    /// </summary>
    [Fact]
    public async Task Deleting_the_account_removes_post_images_and_pending_uploads()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "post-image-account");
        var (_, imageUrl) = await _images.PublishedAsync(user, (await _images.UploadedAsync(user)).Key);
        var (_, pendingUrl) = await _images.UploadedAsync(user);

        Assert.Equal(HttpStatusCode.NoContent, (await new CommunityTestClient(factory, _client).DeleteAccountAsync(user)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await PostImageClient.StatusOfAsync(imageUrl));
        Assert.Equal(HttpStatusCode.NotFound, await PostImageClient.StatusOfAsync(pendingUrl));
    }

    /// <summary>The fourth path: a deleted community takes everyone's posts in it, and their images go after the commit.</summary>
    [Fact]
    public async Task Deleting_a_community_removes_the_images_of_its_posts()
    {
        var owner = await factory.CreateLoggedInUserAsync(_client, "post-image-community-owner");
        var member = await factory.CreateLoggedInUserAsync(_client, "post-image-community-member");
        var communities = new CommunityTestClient(factory, _client);
        var communityId = await communities.CreateAsync(owner, "Zdjęcia w społeczności");
        await communities.JoinedAsync(member, communityId);
        var (key, url) = await _images.UploadedAsync(member);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostJsonAsync($"/api/v1/communities/{communityId}/posts", new { content = "ze zdjęciem", imageKey = key }, member.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/communities/{communityId}", owner.Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, await PostImageClient.StatusOfAsync(url));
    }
}
