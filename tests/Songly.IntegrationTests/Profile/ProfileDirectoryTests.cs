using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Songly.Profile.Contracts;
using Songly.Tests.Shared;

namespace Songly.IntegrationTests.Profile;

/// <summary>
/// The profile module's boundary for other modules, used the way another module will use it —
/// through the contract, from the container. Counts are tested in <c>GenrePopularityCacheTests</c>.
/// </summary>
public sealed class ProfileDirectoryTests(SonglyApiFactory factory)
{
    private static readonly Guid Pop = Guid.Parse("a5a9f290-1111-4a2e-8b1a-000000000001");
    private static readonly Guid Jazz = Guid.Parse("a5a9f290-1111-4a2e-8b1a-000000000005");

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Display_names_are_given_for_known_users_and_unknown_ones_are_absent()
    {
        var user = await factory.CreateLoggedInUserAsync(_client, "directory-names");
        var unknown = Guid.NewGuid();

        var names = await Directory(d => d.DisplayNamesAsync([user.Id, unknown], TestContext.Current.CancellationToken));

        Assert.Equal(user.User.Email.Split('@')[0], names[user.Id]);
        Assert.False(names.ContainsKey(unknown));
    }

    /// <summary>"No avatar" is an absent key, so a consumer gets <c>null</c> for it.</summary>
    [Fact]
    public async Task Avatar_urls_are_given_only_for_users_with_an_avatar()
    {
        var withAvatar = await factory.CreateLoggedInUserAsync(_client, "directory-avatar");
        var withoutAvatar = await factory.CreateLoggedInUserAsync(_client, "directory-no-avatar");
        var file = new ByteArrayContent(TestImages.Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/profiles/me/avatar") { Content = new MultipartFormDataContent { { file, "file", "ja.png" } } };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", withAvatar.Token);
        var url = (await (await _client.SendAsync(request, TestContext.Current.CancellationToken)).ReadJsonAsync()).GetProperty("avatarUrl").GetString();

        var avatars = await Directory(d => d.AvatarUrlsAsync([withAvatar.Id, withoutAvatar.Id], TestContext.Current.CancellationToken));

        Assert.Equal(url, avatars[withAvatar.Id]);
        Assert.False(avatars.ContainsKey(withoutAvatar.Id));
    }

    [Fact]
    public async Task Favourite_genres_are_given_per_user()
    {
        var both = await factory.CreateLoggedInUserAsync(_client, "directory-genres-both");
        var none = await factory.CreateLoggedInUserAsync(_client, "directory-genres-none");
        await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { Pop, Jazz } }, both.Token);

        var genres = await Directory(d => d.FavoriteGenreIdsByUserAsync([both.Id, none.Id], TestContext.Current.CancellationToken));

        Assert.Equal(new HashSet<Guid> { Pop, Jazz }, genres[both.Id]);
        Assert.False(genres.ContainsKey(none.Id));
    }

    [Fact]
    public async Task Users_with_any_of_the_genres_are_found()
    {
        var jazzFan = await factory.CreateLoggedInUserAsync(_client, "directory-jazz");
        var popFan = await factory.CreateLoggedInUserAsync(_client, "directory-pop");
        await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { Jazz } }, jazzFan.Token);
        await _client.PutJsonAsync("/api/v1/profiles/me/genres", new { genreIds = new[] { Pop } }, popFan.Token);

        var jazzFans = await Directory(d => d.UserIdsWithAnyGenreAsync([Jazz], TestContext.Current.CancellationToken));

        Assert.Contains(jazzFan.Id, jazzFans);
        Assert.DoesNotContain(popFan.Id, jazzFans);
    }

    /// <summary>An empty question has an empty answer — not every row, and not an SQL error.</summary>
    [Fact]
    public async Task Empty_questions_have_empty_answers()
    {
        Assert.Empty(await Directory(d => d.DisplayNamesAsync([], TestContext.Current.CancellationToken)));
        Assert.Empty(await Directory(d => d.UserIdsWithAnyGenreAsync([], TestContext.Current.CancellationToken)));
        Assert.Empty(await Directory(d => d.FavoriteGenreIdsByUserAsync([], TestContext.Current.CancellationToken)));
        Assert.Empty(await Directory(d => d.GenreUserCountsAsync([], TestContext.Current.CancellationToken)));
    }

    private async Task<T> Directory<T>(Func<IProfileDirectory, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IProfileDirectory>());
    }
}
