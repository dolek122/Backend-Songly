using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using Songly.Migrations;

namespace Songly.IntegrationTests;

public sealed partial class InfrastructureTests(SonglyApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Every_migration_script_has_been_applied()
    {
        var scripts = MigrationScripts();
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("SELECT count(*) FROM schema_versions", connection);
        var applied = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(scripts.Count, applied);
    }

    [Theory]
    [InlineData("users")]
    [InlineData("profiles")]
    [InlineData("posts")]
    [InlineData("communities")]
    [InlineData("friendships")]
    [InlineData("music_provider_links")]
    [InlineData("analytics_events")]
    [InlineData("notifications")]
    [InlineData("moderation_log_entries")]
    public async Task Schema_contains_the_tables_of_every_module(string table)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("SELECT to_regclass(@table) IS NOT NULL", connection);
        command.Parameters.AddWithValue("table", $"public.{table}");

        Assert.True((bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    /// <summary>
    /// Two scripts with the same version would compare as equal, and one of them would be
    /// treated as already applied. A gap means a script was lost while porting.
    /// </summary>
    [Fact]
    public void Migration_versions_are_unique_and_without_gaps()
    {
        var versions = MigrationScripts().Select(name => int.Parse(Version().Match(name).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Order().ToList();

        Assert.Equal(Enumerable.Range(1, versions.Count), versions);
    }

    [Fact]
    public async Task Health_endpoint_reports_a_reachable_database()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenApi_document_is_published()
    {
        var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Instants and enums keep the shape the original Java API gave them.</summary>
    [Fact]
    public async Task Json_writes_instants_in_utc_and_enums_as_upper_snake_case()
    {
        var json = await _client.GetFromJsonAsync<JsonElement>("/probe/json-shape", TestContext.Current.CancellationToken);

        Assert.Equal("2026-11-02T12:30:15.123Z", json.GetProperty("at").GetString());
        Assert.Equal("MODERATOR_GRANTED", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Request_id_is_generated_when_the_client_sends_none()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.True(Guid.TryParse(response.Headers.GetValues("X-Request-Id").Single(), out _));
    }

    /// <summary>The incoming id lands on every log line, so it must not be able to carry anything else.</summary>
    [Theory]
    [InlineData("mobile-1234.abc_D", "mobile-1234.abc_D")]
    [InlineData("abc\"}<script> x", "abcscriptx")]
    public async Task Incoming_request_id_is_honoured_but_sanitised(string incoming, string expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Request-Id", incoming);

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task Incoming_request_id_is_truncated()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Request-Id", new string('a', 200));

        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(new string('a', 64), response.Headers.GetValues("X-Request-Id").Single());
    }

    private static List<string> MigrationScripts() =>
        typeof(DatabaseMigrator).Assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql", StringComparison.Ordinal)).ToList();

    [GeneratedRegex(@"\.V(\d+)__")]
    private static partial Regex Version();
}
