using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Songly.IntegrationTests;

/// <summary>
/// Every failure, including the ones the framework produces on its own, comes back in the single
/// error format with the same codes and messages as the original backend.
/// </summary>
public sealed class ErrorFormatTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Unknown_route_is_404_not_found()
    {
        var tokens = await _client.LoginAsync(await factory.CreateUserAsync("not-found"));

        var response = await _client.GetAsync("/no-such-endpoint", tokens.AccessToken);

        var error = await AssertError(response, HttpStatusCode.NotFound, "NOT_FOUND", "Nie znaleziono zasobu: /no-such-endpoint");
        Assert.Equal("/no-such-endpoint", error.GetProperty("path").GetString());
    }

    /// <summary>
    /// Without a token, an unknown route is 401 like any closed one, exactly as in the original:
    /// the closed-by-default policy applies before "not found" does.
    /// </summary>
    [Fact]
    public async Task Unknown_route_without_a_token_is_401()
    {
        var response = await _client.GetAsync("/no-such-endpoint", TestContext.Current.CancellationToken);

        await AssertError(response, HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "Wymagane uwierzytelnienie");
    }

    [Fact]
    public async Task Invalid_fields_are_400_validation_error_with_every_field_named()
    {
        var response = await _client.PostJsonAsync("/probe/validated", new { email = "not-an-address", password = "short" });

        await AssertError(
            response,
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR",
            "email: musi być poprawnym adresem e-mail; password: musi mieć od 8 do 72 znaków");
    }

    [Fact]
    public async Task Missing_fields_are_400_validation_error()
    {
        var response = await _client.PostJsonAsync("/probe/validated", new { });

        await AssertError(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "email: nie może być puste; password: nie może być puste");
    }

    /// <summary>
    /// 40 Polish letters are well within 72 characters but take 80 bytes; bcrypt would only see
    /// the first 72, so the limit counts bytes.
    /// </summary>
    [Fact]
    public async Task Byte_limit_counts_utf8_bytes_not_characters()
    {
        var response = await _client.PostJsonAsync("/probe/validated", new { email = "a@songly.io", password = new string('ą', 40) });

        await AssertError(
            response,
            HttpStatusCode.BadRequest,
            "VALIDATION_ERROR",
            "password: może mieć najwyżej 72 bajtów (litery takie jak ą czy ę zajmują po 2)");
    }

    /// <summary>
    /// With a token. Routing answers a wrong method with an endpoint of its own, which carries
    /// none of the route's metadata — so for an anonymous caller the closed-by-default policy
    /// answers first (401), even on a public route.
    /// </summary>
    [Fact]
    public async Task Known_route_with_wrong_method_is_405()
    {
        var tokens = await _client.LoginAsync(await factory.CreateUserAsync("wrong-method"));
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/probe/query?name=x");
        request.Headers.Authorization = new("Bearer", tokens.AccessToken);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertError(response, HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED", "Metoda DELETE nie jest obsługiwana dla tego zasobu");
    }

    [Fact]
    public async Task Api_exception_carries_its_own_status_and_code()
    {
        var response = await _client.GetAsync("/probe/api-exception", TestContext.Current.CancellationToken);

        await AssertError(response, HttpStatusCode.Conflict, "PROBE_CONFLICT", "Konflikt testowy");
    }

    [Fact]
    public async Task Unexpected_exception_is_500_without_details_and_keeps_the_request_id()
    {
        var response = await _client.GetAsync("/probe/unexpected", TestContext.Current.CancellationToken);

        await AssertError(response, HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Nieoczekiwany błąd serwera");
        // The 500 is the response a user reports, so it must carry the correlation id too.
        Assert.True(response.Headers.Contains("X-Request-Id"));
    }

    [Fact]
    public async Task Malformed_json_is_400_malformed_request()
    {
        var response = await PostRaw("/probe/body", "{ not json", "application/json");

        await AssertError(response, HttpStatusCode.BadRequest, "MALFORMED_REQUEST", "Treść żądania jest pusta lub nie jest poprawnym JSON-em");
    }

    [Fact]
    public async Task Empty_body_is_400_malformed_request()
    {
        var response = await PostRaw("/probe/body", string.Empty, "application/json");

        await AssertError(response, HttpStatusCode.BadRequest, "MALFORMED_REQUEST", "Treść żądania jest pusta lub nie jest poprawnym JSON-em");
    }

    /// <summary>With a token, for the same reason as the 405 test.</summary>
    [Fact]
    public async Task Body_in_unsupported_format_is_415()
    {
        var tokens = await _client.LoginAsync(await factory.CreateUserAsync("wrong-content-type"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/probe/body") { Content = new StringContent("name=x", Encoding.UTF8, "text/plain") };
        request.Headers.Authorization = new("Bearer", tokens.AccessToken);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertError(response, HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE", "Nieobsługiwany format treści żądania");
    }

    [Fact]
    public async Task Missing_query_parameter_is_400_missing_parameter()
    {
        var response = await _client.GetAsync("/probe/query", TestContext.Current.CancellationToken);

        await AssertError(response, HttpStatusCode.BadRequest, "MISSING_PARAMETER", "Brak wymaganego parametru: name");
    }

    [Fact]
    public async Task Malformed_uuid_in_path_is_400_invalid_parameter()
    {
        var response = await _client.GetAsync("/probe/items/not-a-uuid", TestContext.Current.CancellationToken);

        await AssertError(response, HttpStatusCode.BadRequest, "INVALID_PARAMETER", "Nieprawidłowa wartość parametru: id");
    }

    [Fact]
    public async Task Valid_requests_still_succeed()
    {
        var body = await _client.PostAsJsonAsync("/probe/body", new { name = "Ala" }, TestContext.Current.CancellationToken);
        var item = await _client.GetAsync($"/probe/items/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, body.StatusCode);
        Assert.Equal(HttpStatusCode.OK, item.StatusCode);
    }

    private Task<HttpResponseMessage> PostRaw(string path, string body, string contentType) =>
        _client.PostAsync(path, new StringContent(body, Encoding.UTF8, contentType), TestContext.Current.CancellationToken);

    private static async Task<JsonElement> AssertError(HttpResponseMessage response, HttpStatusCode status, string code, string message)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
        Assert.EndsWith("Z", error.GetProperty("timestamp").GetString(), StringComparison.Ordinal);
        return error;
    }
}
