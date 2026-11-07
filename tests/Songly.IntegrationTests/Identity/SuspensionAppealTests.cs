using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Songly.IntegrationTests.Identity;

/// <summary>
/// Appeals against a suspension (original ADR 0023). Ported from the original
/// <c>SuspensionAppealIntegrationTest</c>. Knowing the decision without being able to contest it
/// would only be a better-described wall. The e-mail to administrators ("there is an appeal to
/// read", never its text) is the notification module's, tested in <c>SuspensionMailTests</c>.
/// </summary>
public sealed class SuspensionAppealTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task Suspend(LoggedInUser admin, Guid target, string reason = "spam") =>
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PutJsonAsync($"/api/v1/admin/users/{target}/suspension", new { reason }, admin.Token)).StatusCode);

    private Task<HttpResponseMessage> Appeal(LoggedInUser user, string text) =>
        _client.PostJsonAsync("/api/v1/auth/me/suspension/appeal", new { text }, user.Token);

    private Task<HttpResponseMessage> Reject(LoggedInUser admin, Guid target) =>
        _client.PostJsonAsync($"/api/v1/admin/users/{target}/suspension/appeal/rejection", new { }, admin.Token);

    private async Task<JsonElement> OwnSuspension(LoggedInUser user) =>
        await (await _client.GetAsync("/api/v1/auth/me/suspension", user.Token)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private async Task<JsonElement> ListedFor(LoggedInUser admin, Guid userId)
    {
        var list = await (await _client.GetAsync("/api/v1/admin/suspensions", admin.Token)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return list.EnumerateArray().Single(row => row.GetProperty("userId").GetGuid() == userId);
    }

    /// <summary>
    /// The suspended user appeals through one of the few endpoints open to them; the text waits
    /// in the administrators' list — the pull channel — and the user sees the state, not the text.
    /// </summary>
    [Fact]
    public async Task An_appeal_reaches_the_admin_list_and_its_text_stays_in_the_pull_channel()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appealing");
        await Suspend(admin, user.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await Appeal(user, "To była pomyłka, proszę o ponowne sprawdzenie.")).StatusCode);

        var row = await ListedFor(admin, user.Id);
        Assert.Equal("To była pomyłka, proszę o ponowne sprawdzenie.", row.GetProperty("appealText").GetString());
        Assert.Equal("PENDING", row.GetProperty("appealStatus").GetString());

        var own = await OwnSuspension(user);
        Assert.Equal("PENDING", own.GetProperty("appealStatus").GetString());
        Assert.False(own.TryGetProperty("appealText", out _));
    }

    /// <summary>One appeal per suspension — the second is a 409, not a second notification.</summary>
    [Fact]
    public async Task A_second_appeal_is_rejected_with_conflict()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-twice");
        await Suspend(admin, user.Id);
        await Appeal(user, "Pierwsze odwołanie");

        await (await Appeal(user, "Drugie odwołanie")).ReadErrorAsync(HttpStatusCode.Conflict, "SUSPENSION_APPEAL_ALREADY_SUBMITTED");
    }

    [Fact]
    public async Task An_appeal_without_an_active_suspension_is_rejected()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var neverSuspended = await factory.CreateLoggedInUserAsync(_client, "never-suspended");
        var expired = await factory.CreateLoggedInUserAsync(_client, "expired-suspension");
        await Suspend(admin, expired.Id);
        await factory.ExecuteSqlAsync("UPDATE users SET suspension_expires_at = now() - interval '1 minute' WHERE id = @id", ("id", expired.Id));

        await (await Appeal(neverSuspended, "Odwołanie")).ReadErrorAsync(HttpStatusCode.Conflict, "NO_SUSPENSION_TO_APPEAL");
        await (await Appeal(expired, "Odwołanie")).ReadErrorAsync(HttpStatusCode.Conflict, "NO_SUSPENSION_TO_APPEAL");
    }

    /// <summary>Accepting an appeal is lifting the suspension — one decision, no separate endpoint.</summary>
    [Fact]
    public async Task Lifting_with_a_pending_appeal_accepts_it()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-accepted");
        await Suspend(admin, user.Id);
        await Appeal(user, "Proszę o zdjęcie");

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/v1/admin/users/{user.Id}/suspension", admin.Token)).StatusCode);

        var own = await OwnSuspension(user);
        Assert.False(own.GetProperty("suspended").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/probe-secured/whoami", user.Token)).StatusCode);
    }

    /// <summary>Rejection keeps the suspension and closes this case: there is no second appeal.</summary>
    [Fact]
    public async Task Rejection_keeps_the_suspension_and_closes_the_case()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-rejected");
        await Suspend(admin, user.Id);
        await Appeal(user, "Proszę o zdjęcie");

        Assert.Equal(HttpStatusCode.NoContent, (await Reject(admin, user.Id)).StatusCode);

        var own = await OwnSuspension(user);
        Assert.True(own.GetProperty("suspended").GetBoolean());
        Assert.Equal("REJECTED", own.GetProperty("appealStatus").GetString());
        await (await Appeal(user, "Jeszcze raz")).ReadErrorAsync(HttpStatusCode.Conflict, "SUSPENSION_APPEAL_ALREADY_SUBMITTED");
        await (await Reject(admin, user.Id)).ReadErrorAsync(HttpStatusCode.NotFound, "SUSPENSION_APPEAL_NOT_FOUND");
    }

    /// <summary>
    /// A new suspension brings a fresh right to appeal, even after a rejection — but correcting the
    /// reason of an ongoing one does <b>not</b> wipe a petition already filed.
    /// </summary>
    [Fact]
    public async Task A_new_suspension_resets_the_appeal_but_editing_an_active_one_does_not()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-reset");
        await Suspend(admin, user.Id, "pierwszy powód");
        await Appeal(user, "Odwołanie");

        await Suspend(admin, user.Id, "poprawiony powód");
        Assert.Equal("PENDING", (await OwnSuspension(user)).GetProperty("appealStatus").GetString());

        await Reject(admin, user.Id);
        await _client.DeleteAsync($"/api/v1/admin/users/{user.Id}/suspension", admin.Token);
        await Suspend(admin, user.Id, "nowa sprawa");

        Assert.Equal(JsonValueKind.Null, (await OwnSuspension(user)).GetProperty("appealStatus").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await Appeal(user, "Nowe odwołanie")).StatusCode);
    }

    /// <summary>
    /// An expired suspension keeps its rejected appeal in the row (expiry is lazy, nothing clears
    /// it). The next suspension is a new case and must not inherit "already appealed".
    /// </summary>
    [Fact]
    public async Task A_new_suspension_after_an_expired_one_brings_a_fresh_right_to_appeal()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-after-expiry");
        await Suspend(admin, user.Id, "pierwsza sprawa");
        await Appeal(user, "Odwołanie");
        await Reject(admin, user.Id);
        await factory.ExecuteSqlAsync("UPDATE users SET suspension_expires_at = now() - interval '1 minute' WHERE id = @id", ("id", user.Id));

        await Suspend(admin, user.Id, "druga sprawa");

        Assert.Equal(JsonValueKind.Null, (await OwnSuspension(user)).GetProperty("appealStatus").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await Appeal(user, "Odwołanie w nowej sprawie")).StatusCode);
    }

    [Theory]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task A_blank_appeal_is_rejected(string? text)
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-blank");
        await Suspend(admin, user.Id);

        await (await _client.PostJsonAsync("/api/v1/auth/me/suspension/appeal", new { text }, user.Token)).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task An_appeal_over_1000_characters_is_rejected()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-long");
        await Suspend(admin, user.Id);

        await (await Appeal(user, new string('x', 1001))).ReadErrorAsync(HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Only_an_admin_rejects_an_appeal()
    {
        var admin = await factory.CreateAdminAsync(_client);
        var user = await factory.CreateLoggedInUserAsync(_client, "appeal-target");
        var bystander = await factory.CreateLoggedInUserAsync(_client, "bystander");
        await Suspend(admin, user.Id);
        await Appeal(user, "Odwołanie");

        await (await Reject(bystander, user.Id)).ReadErrorAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }
}
