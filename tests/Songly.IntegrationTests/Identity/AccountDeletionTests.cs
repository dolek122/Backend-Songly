using System.Net;
using Songly.Identity.Contracts;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Deleting an account from the app (GDPR, original Claude.md §9). Ported from the original
/// <c>AccountDeletionFlowIntegrationTest</c>, <c>AuthServiceTest</c> and
/// <c>AccountSuspensionIntegrationTest</c>.
/// </summary>
/// <remarks>
/// The original's flow test touches every module at once, so a module that forgot its deletion
/// handler shows up as a foreign-key error here rather than in production. Until other modules
/// are ported, a probe handler with its own DbContext stands in for them and proves the mechanism:
/// handlers run inside the deletion's transaction, before the <c>users</c> row goes.
/// </remarks>
public sealed class AccountDeletionTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> DeleteAccount(LoggedInUser user, string? password = null) =>
        _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/me")
            {
                Headers = { Authorization = new("Bearer", user.Token) },
                Content = System.Net.Http.Json.JsonContent.Create(new { password = password ?? user.User.Password }),
            },
            TestContext.Current.CancellationToken);

    private Task<long> CountRows(string table, Guid userId) =>
        factory.QuerySingleAsync<long>($"SELECT count(*) FROM {table} WHERE {(table == "users" ? "id" : "user_id")} = @id", ("id", userId));

    [Fact]
    public async Task Deleting_the_account_removes_it_and_everything_identity_keeps()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "delete-me");
        await _client.LoginAsync(user.User);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(user)).StatusCode);

        Assert.Equal(0L, await CountRows("users", user.Id));
        Assert.Equal(0L, await CountRows("sessions", user.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.TryLoginAsync(user.User.Email, user.User.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(user.Tokens.RefreshToken)).StatusCode);
    }

    /// <summary>A valid access token alone is not enough for an irreversible action — it may have been stolen.</summary>
    [Fact]
    public async Task A_wrong_password_deletes_nothing()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "delete-wrong");

        await (await DeleteAccount(user, "not-my-password")).ReadErrorAsync(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");

        Assert.Equal(1L, await CountRows("users", user.Id));
    }

    [Fact]
    public async Task The_password_is_required()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "delete-blank");

        await (await DeleteAccount(user, "")).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Deleting_requires_authentication()
    {
        var response = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/v1/auth/me") { Content = System.Net.Http.Json.JsonContent.Create(new { password = "x" }) },
            TestContext.Current.CancellationToken);

        await response.ReadErrorAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    /// <summary>The right to erasure survives a suspension — a sanction must not take it away.</summary>
    [Fact]
    public async Task A_suspended_account_can_still_delete_itself()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "delete-suspended");
        await factory.SuspendAsync(user.User);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(user)).StatusCode);

        Assert.Equal(0L, await CountRows("users", user.Id));
    }

    /// <summary>
    /// An unused reset or deletion link must not block the deletion — their rows reference users too.
    /// </summary>
    [Fact]
    public async Task Pending_links_do_not_block_the_deletion()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "delete-links");
        await _client.PostJsonAsync("/api/v1/auth/password/reset-request", new { email = user.User.Email });
        await _client.PostAsync(
            "/account-deletion",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = user.User.Email }),
            TestContext.Current.CancellationToken);
        Assert.Equal(1L, await CountRows("password_reset_tokens", user.Id));
        Assert.Equal(1L, await CountRows("account_deletion_tokens", user.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAccount(user)).StatusCode);

        Assert.Equal(0L, await CountRows("users", user.Id));
    }

    /// <summary>
    /// Other modules clean up first, while the account still exists — their foreign keys point to
    /// <c>users</c>, so after the row is gone it would be too late.
    /// </summary>
    [Fact]
    public async Task Other_modules_handle_the_deletion_before_the_account_row_goes()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "delete-order");

        await DeleteAccount(user);

        Assert.True(ProbeDeletionHandler.UserRowPresentWhenHandled[user.Id]);
    }

    /// <summary>
    /// The heart of it: every module's part and the deletion itself are one transaction. A module
    /// that fails rolls everything back — including what that module had already deleted through
    /// its own DbContext — so no account is ever left half-deleted.
    /// </summary>
    [Fact]
    public async Task A_failing_module_rolls_the_whole_deletion_back()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "delete-rollback");
        ProbeDeletionHandler.FailFor[user.Id] = 0;

        await (await DeleteAccount(user)).ReadErrorAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");

        Assert.Equal(1L, await CountRows("users", user.Id));
        Assert.Equal(1L, await CountRows("sessions", user.Id));
        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(user.Tokens.RefreshToken)).StatusCode);
    }
}
