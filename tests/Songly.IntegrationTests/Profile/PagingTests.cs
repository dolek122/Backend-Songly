using System.Text.Json;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// <c>?page=&amp;size=</c> read the way the original's Spring Data read it — nothing is rejected,
/// everything out of range is brought into range — and the <c>PagedModel</c> shape of the
/// response. Run against the genre list: fifteen rows that come with the schema and never change.
/// </summary>
public sealed class PagingTests(SonglyApiFactory factory) : IAsyncLifetime
{
    private const int Genres = 15;

    private readonly HttpClient _client = factory.CreateClient();
    private LoggedInUser? _user;

    public async ValueTask InitializeAsync() => _user = await factory.CreateLoggedInUserAsync(_client, "paging");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_page_says_where_it_is()
    {
        var page = await Genre("?page=1&size=4");

        Assert.Equal(4, page.GetProperty("content").GetArrayLength());
        Assert.Equal(4, Meta(page, "size"));
        Assert.Equal(1, Meta(page, "number"));
        Assert.Equal(Genres, Meta(page, "totalElements"));
        Assert.Equal(4, Meta(page, "totalPages"));
    }

    /// <summary>Pages do not overlap and leave nothing out: the order is fixed.</summary>
    [Fact]
    public async Task Consecutive_pages_cover_the_list_once()
    {
        var all = new List<Guid>();
        for (var number = 0; number < 4; number++)
        {
            all.AddRange((await Genre($"?page={number}&size=4")).GetProperty("content").EnumerateArray().Select(g => g.GetProperty("id").GetGuid()));
        }

        Assert.Equal(Genres, all.Distinct().Count());
        Assert.Equal(all, (await Genre("?size=100")).GetProperty("content").EnumerateArray().Select(g => g.GetProperty("id").GetGuid()));
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData("?size=0", 20)]
    [InlineData("?size=-5", 20)]
    [InlineData("?size=abc", 20)]
    [InlineData("?size=500", 100)]
    [InlineData("?size=99999999999", 20)]
    public async Task The_size_is_brought_into_range(string query, int size) =>
        Assert.Equal(size, Meta(await Genre(query), "size"));

    [Theory]
    [InlineData("?page=-1")]
    [InlineData("?page=abc")]
    [InlineData("?page=99999999999")]
    public async Task A_page_number_that_is_not_one_is_the_first_page(string query) =>
        Assert.Equal(0, Meta(await Genre(query), "number"));

    [Fact]
    public async Task A_page_past_the_end_is_empty_and_still_counts_everything()
    {
        var page = await Genre($"?page={int.MaxValue}&size=100");

        Assert.Equal(0, page.GetProperty("content").GetArrayLength());
        Assert.Equal(int.MaxValue, Meta(page, "number"));
        Assert.Equal(Genres, Meta(page, "totalElements"));
    }

    private async Task<JsonElement> Genre(string query) =>
        await (await _client.GetAsync($"/api/v1/genres{query}", _user!.Token)).ReadJsonAsync();

    private static long Meta(JsonElement page, string name) => page.GetProperty("page").GetProperty(name).GetInt64();
}
