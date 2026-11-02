using System.Net;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Deleting an account without the app, through the web page (original ADR 0043) — the path Google
/// Play requires of every app with accounts. Ported from the original
/// <c>AccountDeletionPageIntegrationTest</c>.
/// </summary>
public sealed class AccountDeletionPageTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> PostForm(string path, string field, string value, HttpClient? client = null) =>
        (client ?? _client).PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { [field] = value }), TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> RequestLink(string email, HttpClient? client = null) => PostForm("/account-deletion", "email", email, client);

    private Task<HttpResponseMessage> Confirm(string token) => PostForm("/account-deletion/confirm", "token", token);

    private static async Task<string> Html(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Public, and it cannot be framed by someone else's page or made to load anything from outside.</summary>
    [Fact]
    public async Task The_start_page_is_public_html_with_restrictive_headers()
    {
        var response = await _client.GetAsync("/account-deletion", TestContext.Current.CancellationToken);

        var html = await Html(response, HttpStatusCode.OK);
        Assert.Contains("Usuwanie konta Songly", html, StringComparison.Ordinal);
        var policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("form-action 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", policy, StringComparison.Ordinal);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    /// <summary>
    /// The whole path. Opening the link only shows a button — mail scanners open links on their own —
    /// and only pressing it deletes the account.
    /// </summary>
    [Fact]
    public async Task The_link_from_the_mail_deletes_the_account_only_after_pressing_the_button()
    {
        var user = await factory.CreateUserAsync("page-delete");

        Assert.Contains("Sprawdź skrzynkę", await Html(await RequestLink(user.Email), HttpStatusCode.OK), StringComparison.Ordinal);
        var token = factory.Mails.TokenSentTo(user.Email);

        var confirmPage = await Html(await _client.GetAsync($"/account-deletion/confirm?token={token}", TestContext.Current.CancellationToken), HttpStatusCode.OK);
        Assert.Contains("Usuń konto na stałe", confirmPage, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);

        Assert.Contains("Konto usunięte", await Html(await Confirm(token), HttpStatusCode.OK), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
    }

    /// <summary>The same page for an address with an account and without — the page is not a list of accounts.</summary>
    [Fact]
    public async Task An_unknown_address_gets_the_same_page_and_no_mail()
    {
        var known = await factory.CreateUserAsync("page-known");
        var unknown = TestAccounts.UniqueEmail("page-nobody");

        var knownPage = await Html(await RequestLink(known.Email), HttpStatusCode.OK);
        var unknownPage = await Html(await RequestLink(unknown), HttpStatusCode.OK);

        Assert.Equal(knownPage, unknownPage);
        Assert.Empty(factory.Mails.SentTo(unknown));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("   ")]
    public async Task An_invalid_address_shows_the_form_again(string email)
    {
        var html = await Html(await RequestLink(email), HttpStatusCode.BadRequest);

        Assert.Contains("Podaj poprawny adres e-mail.", html, StringComparison.Ordinal);
        Assert.Contains("<form", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_token_deletes_nothing()
    {
        Assert.Contains("Ten link jest nieważny", await Html(await _client.GetAsync("/account-deletion/confirm?token=never-issued", TestContext.Current.CancellationToken), HttpStatusCode.NotFound), StringComparison.Ordinal);
        Assert.Contains("Ten link jest nieważny", await Html(await Confirm("never-issued"), HttpStatusCode.NotFound), StringComparison.Ordinal);
        await Html(await _client.GetAsync("/account-deletion/confirm", TestContext.Current.CancellationToken), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_expired_link_is_rejected()
    {
        var user = await factory.CreateUserAsync("page-expired");
        await RequestLink(user.Email);
        var token = factory.Mails.TokenSentTo(user.Email);
        await factory.ExecuteSqlAsync("UPDATE account_deletion_tokens SET expires_at = now() - interval '1 minute' WHERE user_id = @id", ("id", user.Id));

        await Html(await Confirm(token), HttpStatusCode.NotFound);

        Assert.Equal(HttpStatusCode.OK, (await _client.TryLoginAsync(user.Email, user.Password)).StatusCode);
    }

    /// <summary>
    /// The link in the mail comes from configuration, never from the request's Host header — a forged
    /// header would otherwise send the victim's deletion token to the attacker's server.
    /// </summary>
    [Fact]
    public async Task The_link_ignores_the_host_the_request_came_to()
    {
        var user = await factory.CreateUserAsync("page-host");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account-deletion")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = user.Email }),
        };
        request.Headers.Host = "attacker.example";

        await _client.SendAsync(request, TestContext.Current.CancellationToken);

        var body = factory.Mails.LastTo(user.Email).Body;
        Assert.Contains("http://localhost:8080/account-deletion/confirm?token=", body, StringComparison.Ordinal);
        Assert.DoesNotContain("attacker.example", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_address_is_normalized()
    {
        var user = await factory.CreateUserAsync("page-case");

        await RequestLink($" {user.Email.ToUpperInvariant()} ");

        Assert.Equal("Songly — potwierdź usunięcie konta", factory.Mails.LastTo(user.Email).Subject);
    }

    /// <summary>The same protection of someone's inbox as for password reset: the page is public.</summary>
    [Fact]
    public async Task Repeated_requests_within_the_interval_send_one_message()
    {
        var user = await factory.CreateUserAsync("page-throttle");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Html(await RequestLink(user.Email), HttpStatusCode.OK);
        }

        Assert.Single(factory.Mails.SentTo(user.Email));
    }

    /// <summary>The page is for people, not for the mobile client — it stays out of the API contract.</summary>
    [Fact]
    public async Task The_pages_are_not_in_the_openapi_document()
    {
        var document = await (await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/account-deletion", document, StringComparison.Ordinal);
    }
}
