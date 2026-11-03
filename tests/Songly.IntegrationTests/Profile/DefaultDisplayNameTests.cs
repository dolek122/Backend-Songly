using System.Text.RegularExpressions;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// The default display name reveals nothing of the e-mail address (original ADR 0046). Ported from
/// the original <c>DefaultDisplayNameIntegrationTest</c>; it registers without
/// <c>CreateUserAsync</c>, which names accounts after their address — with it, this test would test
/// the helper, not the application.
/// </summary>
public sealed partial class DefaultDisplayNameTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_new_profile_gets_a_neutral_name_that_reveals_nothing_from_the_address()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var email = $"jan.kowalski{tag}@songly.io";
        var tokens = await _client.RegisterAndVerifyAsync(factory.Mails, email);

        var me = await (await _client.GetAsync("/api/v1/profiles/me", tokens.AccessToken)).ReadJsonAsync();
        var displayName = me.GetProperty("displayName").GetString()!;
        Assert.Matches(NeutralName(), displayName);
        Assert.DoesNotContain("kowalski", displayName, StringComparison.OrdinalIgnoreCase);

        // The search is where the old name handed the address to everyone: a fragment of the
        // address must not find this account any more.
        var searcher = await factory.CreateLoggedInUserAsync(_client, "default-name-searcher");
        Assert.Empty(await _client.SearchAsync(searcher, $"kowalski{tag}"));
    }

    [GeneratedRegex(@"^Użytkownik \d{6}$")]
    private static partial Regex NeutralName();
}
