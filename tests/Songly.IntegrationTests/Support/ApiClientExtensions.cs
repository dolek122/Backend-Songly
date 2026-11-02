using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Songly.IntegrationTests;

public sealed record Tokens(string AccessToken, string RefreshToken);

public static class ApiClientExtensions
{
    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string path, string? accessToken) =>
        client.SendAsync(Request(HttpMethod.Get, path, accessToken, body: null), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, object body, string? accessToken = null) =>
        client.SendAsync(Request(HttpMethod.Post, path, accessToken, body), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PutJsonAsync(this HttpClient client, string path, object body, string? accessToken = null) =>
        client.SendAsync(Request(HttpMethod.Put, path, accessToken, body), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> TryLoginAsync(this HttpClient client, string email, string password) =>
        client.PostJsonAsync("/api/v1/auth/login", new { email, password });

    public static async Task<Tokens> LoginAsync(this HttpClient client, TestUser user, string? password = null)
    {
        var response = await client.TryLoginAsync(user.Email, password ?? user.Password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Tokens>(TestContext.Current.CancellationToken))!;
    }

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    public static async Task<Tokens> ReadTokensAsync(this HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Tokens>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Asserts the status and returns the error body.</summary>
    public static async Task<JsonElement> ReadErrorAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(code, error.GetProperty("code").GetString());
        return error;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? accessToken, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
