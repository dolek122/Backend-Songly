using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Songly.Common.Media;
using Songly.Tests.Shared;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// Avatar upload end to end (original ADR 0010, 0047), against a real MinIO. Ported from the
/// original <c>AvatarUploadIntegrationTest</c>.
/// </summary>
/// <remarks>
/// The question these answer is "can the file really be loaded from the returned address" — a
/// fake storage would only confirm itself. So the returned URL is fetched <b>by a plain HTTP client,
/// without authentication</b>, exactly as a browser or the mobile app does.
/// </remarks>
public sealed class AvatarUploadTests(SonglyApiFactory factory)
{
    private static readonly HttpClient Anonymous = new();

    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>The point of it all: the file uploaded through the API is in the storage, at the returned address.</summary>
    [Fact]
    public async Task An_uploaded_avatar_can_be_fetched_publicly_at_the_returned_url()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-happy");

        var url = await AvatarUrlOf(await Upload(user, TestImages.Png(), "ja.png", "image/png"));

        using var fetched = await Fetch(url);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(TestImages.Png(), await fetched.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith($"{factory.StoragePublicBaseUrl}/avatars/{user.Id}/", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// An avatar is public, and a camera photo carries in its EXIF the place where it was taken
    /// (original ADR 0047). What is checked is the file in the storage, not what the API returned.
    /// </summary>
    [Fact]
    public async Task An_uploaded_avatar_is_stored_without_camera_metadata()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-exif");

        var url = await AvatarUrlOf(await Upload(user, TestImages.JpegWithExif(6), "z-aparatu.jpg", "image/jpeg"));

        var stored = await Anonymous.GetByteArrayAsync(url, TestContext.Current.CancellationToken);
        Assert.False(TestImages.Contains(stored, TestImages.Secret));
        Assert.Equal(SanitizedImage.From(TestImages.JpegWithExif(6)).Content, stored);
    }

    /// <summary>
    /// The format comes from the <b>content</b>, not from the header or the file name. Here both lie
    /// — the file is a JPEG, the client says it is a PNG called <c>.html</c>. The key's extension must
    /// come from the detection, or someone could get an address serving their HTML from our domain.
    /// </summary>
    [Fact]
    public async Task The_extension_and_content_type_from_the_client_are_ignored()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-liar");

        var url = await AvatarUrlOf(await Upload(user, TestImages.Jpeg(), "zlosliwy.html", "text/html"));

        Assert.EndsWith(".jpg", url, StringComparison.Ordinal);
        using var fetched = await Fetch(url);
        Assert.Equal("image/jpeg", fetched.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// A file that is not an image is rejected, even when its header claims otherwise — the only
    /// reason the bytes go through the backend at all rather than straight to the storage.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_not_an_image_is_rejected_even_when_it_claims_to_be_one()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-notimage");

        await (await Upload(user, Encoding.ASCII.GetBytes("MZ to nie jest obraz"), "malware.png", "image/png"))
            .ReadErrorAsync(HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_IMAGE");

        Assert.Null(await AvatarOf(user));
    }

    [Fact]
    public async Task An_empty_file_is_rejected_with_its_own_code()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-empty");

        await (await Upload(user, [], "puste.png", "image/png")).ReadErrorAsync(HttpStatusCode.BadRequest, "EMPTY_UPLOAD");
    }

    /// <summary>
    /// The limit is checked while the request is read, so a file that is too large is never
    /// buffered whole. The original could not test this — its test server took the file ready-made.
    /// </summary>
    [Fact]
    public async Task A_file_over_five_megabytes_is_rejected()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-large");
        byte[] large = [.. TestImages.Png(), .. new byte[UploadLimits.MaxFileBytes]];

        var error = await (await Upload(user, large, "duze.png", "image/png")).ReadErrorAsync(HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE");

        Assert.Equal("Przesłany plik jest za duży", error.GetProperty("message").GetString());
        Assert.Null(await AvatarOf(user));
    }

    [Fact]
    public async Task A_request_without_the_file_part_is_a_bad_request()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-nopart");
        using var form = new MultipartFormDataContent { { new StringContent("x"), "other" } };

        var error = await (await Send(user, form)).ReadErrorAsync(HttpStatusCode.BadRequest, "MISSING_PARAMETER");

        Assert.Equal("Brak wymaganej części żądania: file", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_request_that_is_not_multipart_is_unsupported()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-json");

        await (await _client.PostJsonAsync("/api/v1/profiles/me/avatar", new { file = "x" }, user.Token))
            .ReadErrorAsync(HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
    }

    /// <summary>
    /// Replacing the avatar deletes the previous file — otherwise every change of photo would leave
    /// another copy in the storage that nothing points to and no row remembers.
    /// </summary>
    [Fact]
    public async Task Replacing_the_avatar_removes_the_previous_file()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-replace");
        var first = await AvatarUrlOf(await Upload(user, TestImages.Png(), "stary.png", "image/png"));

        var second = await AvatarUrlOf(await Upload(user, TestImages.Jpeg(), "nowy.jpg", "image/jpeg"));

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.OK, (await Fetch(second)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(first)).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_avatar_clears_the_profile_and_the_file()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-delete");
        var url = await AvatarUrlOf(await Upload(user, TestImages.Png(), "ja.png", "image/png"));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/v1/profiles/me/avatar", user.Token)).StatusCode);

        Assert.Null(await AvatarOf(user));
        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(url)).StatusCode);
    }

    /// <summary>Every view of a profile carries the same address.</summary>
    [Fact]
    public async Task The_avatar_shows_in_the_public_profile_the_search_and_the_admin_summaries()
    {
        var admin = await factory.CreateAdminAsync(_client, "avatar-admin");
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-everywhere");
        var name = $"Awatar {Guid.NewGuid():N}";
        await _client.RenameAsync(user, name);
        var url = await AvatarUrlOf(await Upload(user, TestImages.Png(), "ja.png", "image/png"));

        var publicProfile = await (await _client.GetAsync($"/api/v1/profiles/{user.Id}", admin.Token)).ReadJsonAsync();
        var hit = (await (await _client.GetAsync($"/api/v1/profiles/search?query={Uri.EscapeDataString(name)}", admin.Token)).ReadJsonAsync()).GetProperty("content")[0];
        var summary = (await (await _client.GetAsync($"/api/v1/admin/profiles?userIds={user.Id}", admin.Token)).ReadJsonAsync())[0];

        Assert.Equal(url, publicProfile.GetProperty("avatarUrl").GetString());
        Assert.Equal(url, hit.GetProperty("avatarUrl").GetString());
        Assert.Equal(url, summary.GetProperty("avatarUrl").GetString());
    }

    /// <summary>The storage failing is a 502 — the request was fine — and the profile stays as it was.</summary>
    [Fact]
    public async Task A_storage_failure_is_a_bad_gateway_and_changes_nothing()
    {
        using var app = factory.WithSettings(("Songly:Storage:Endpoint", "http://127.0.0.1:1"), ("Songly:Storage:Timeout", "00:00:02"));
        var client = app.CreateClient();
        var user = await factory.CreateLoggedInUserAsync(client, "avatar-storage-down");

        await (await Upload(client, user, TestImages.Png(), "ja.png", "image/png")).ReadErrorAsync(HttpStatusCode.BadGateway, "STORAGE_ERROR");

        Assert.Null(await AvatarOf(user));
    }

    /// <summary>The file goes with the account — after the deletion committed.</summary>
    [Fact]
    public async Task Deleting_the_account_deletes_the_avatar_file()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-account-deleted");
        var url = await AvatarUrlOf(await Upload(user, TestImages.Png(), "ja.png", "image/png"));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(user)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Fetch(url)).StatusCode);
    }

    /// <summary>
    /// Why the file waits for the commit (original Claude.md §9): deleted inside the transaction, a
    /// rolled-back account deletion would leave the profile pointing to a file that is gone — an
    /// avatar that never loads and cannot be recovered.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_account_deletion_keeps_the_avatar_file()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "avatar-deletion-rollback");
        var url = await AvatarUrlOf(await Upload(user, TestImages.Png(), "ja.png", "image/png"));
        ProbeDeletionHandler.FailFor[user.Id] = 0;

        await (await DeleteAccount(user)).ReadErrorAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");

        Assert.Equal(url, await AvatarOf(user));
        Assert.Equal(HttpStatusCode.OK, (await Fetch(url)).StatusCode);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var form = Form(TestImages.Png(), "ja.png", "image/png");

        var response = await _client.PostAsync("/api/v1/profiles/me/avatar", form, TestContext.Current.CancellationToken);

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    private Task<HttpResponseMessage> DeleteAccount(LoggedInUser user)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/me") { Content = JsonContent.Create(new { password = user.User.Password }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private Task<HttpResponseMessage> Upload(LoggedInUser user, byte[] content, string fileName, string contentType) =>
        Upload(_client, user, content, fileName, contentType);

    private static async Task<HttpResponseMessage> Upload(HttpClient client, LoggedInUser user, byte[] content, string fileName, string contentType)
    {
        using var form = Form(content, fileName, contentType);
        return await Send(client, user, form);
    }

    private Task<HttpResponseMessage> Send(LoggedInUser user, MultipartFormDataContent form) => Send(_client, user, form);

    private static Task<HttpResponseMessage> Send(HttpClient client, LoggedInUser user, MultipartFormDataContent form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/profiles/me/avatar") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static MultipartFormDataContent Form(byte[] content, string fileName, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<string> AvatarUrlOf(HttpResponseMessage response) =>
        (await response.ReadJsonAsync()).GetProperty("avatarUrl").GetString()!;

    private async Task<string?> AvatarOf(LoggedInUser user) =>
        (await (await _client.GetAsync("/api/v1/profiles/me", user.Token)).ReadJsonAsync()).GetProperty("avatarUrl").GetString();

    private static Task<HttpResponseMessage> Fetch(string url) => Anonymous.GetAsync(url, TestContext.Current.CancellationToken);
}
