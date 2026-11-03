using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Songly.IntegrationTests.Profile;
using Songly.Tests.Shared;

namespace Songly.IntegrationTests.Content;

/// <summary>Uploading post images and checking the files in the real MinIO, anonymously, as clients fetch them.</summary>
internal sealed class PostImageClient(HttpClient client)
{
    private static readonly HttpClient Anonymous = new();

    public async Task<HttpResponseMessage> UploadAsync(LoggedInUser user, byte[] content, string fileName = "zdjecie.png", string contentType = "image/png")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/posts/image") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A PNG upload: its key and public address.</summary>
    public async Task<(string Key, string Url)> UploadedAsync(LoggedInUser user, byte[]? content = null)
    {
        var upload = await (await UploadAsync(user, content ?? TestImages.Png())).ReadJsonAsync();
        return (upload.GetProperty("key").GetString()!, upload.GetProperty("url").GetString()!);
    }

    public Task<HttpResponseMessage> PublishAsync(LoggedInUser author, string? imageKey, string content = "post ze zdjęciem") =>
        client.PostJsonAsync("/api/v1/posts", new { content, imageKey }, author.Token);

    /// <summary>Publishes and returns the post's id and image address.</summary>
    public async Task<(Guid Id, string ImageUrl)> PublishedAsync(LoggedInUser author, string imageKey)
    {
        var response = await PublishAsync(author, imageKey);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (post.GetProperty("id").GetGuid(), post.GetProperty("imageUrl").GetString()!);
    }

    public static Task<HttpResponseMessage> FetchAsync(string url) => Anonymous.GetAsync(url, TestContext.Current.CancellationToken);

    public static async Task<HttpStatusCode> StatusOfAsync(string url) => (await FetchAsync(url)).StatusCode;
}
