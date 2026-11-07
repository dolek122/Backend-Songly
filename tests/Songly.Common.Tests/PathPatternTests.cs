using Songly.Common.RateLimiting;

namespace Songly.Common.Tests;

/// <summary>The subset of Spring's path patterns the limits are configured with.</summary>
public sealed class PathPatternTests
{
    [Theory]
    [InlineData("/api/v1/auth/login", "/api/v1/auth/login", true)]
    [InlineData("/api/v1/auth/login", "/api/v1/auth/login/", true)]
    [InlineData("/api/v1/auth/login", "/api/v1/auth/logout", false)]
    [InlineData("/api/v1/auth/login", "/api/v1/auth", false)]
    [InlineData("/api/v1/auth/login", "/api/v1/auth/login/extra", false)]
    [InlineData("/api/v1/auth/register", "/api/v1/auth/register/verify", false)]
    [InlineData("/api/v1/auth/login", "/API/v1/auth/login", false)]
    public void A_literal_pattern_matches_that_path_only(string pattern, string path, bool matches) =>
        Assert.Equal(matches, new PathPattern(pattern).Matches(path));

    /// <summary><c>*</c> is exactly one segment — a like on any post, not anything under <c>/posts</c>.</summary>
    [Theory]
    [InlineData("/api/v1/posts/123/likes", true)]
    [InlineData("/api/v1/posts/abc/likes", true)]
    [InlineData("/api/v1/posts/likes", false)]
    [InlineData("/api/v1/posts/1/2/likes", false)]
    [InlineData("/api/v1/posts/123/comments", false)]
    public void A_star_is_one_segment(string path, bool matches) =>
        Assert.Equal(matches, new PathPattern("/api/v1/posts/*/likes").Matches(path));

    /// <summary><c>**</c> is any number of segments, none included — the write budget covers the whole prefix.</summary>
    [Theory]
    [InlineData("/api", true)]
    [InlineData("/api/v1/notifications/read-all", true)]
    [InlineData("/api/v1/communities/1/bans/2", true)]
    [InlineData("/account-deletion", false)]
    [InlineData("/apis/v1", false)]
    public void A_double_star_is_any_number_of_segments(string path, bool matches) =>
        Assert.Equal(matches, new PathPattern("/api/**").Matches(path));

    [Fact]
    public void A_double_star_in_the_middle_matches_across_segments()
    {
        var pattern = new PathPattern("/api/**/likes");

        Assert.True(pattern.Matches("/api/likes"));
        Assert.True(pattern.Matches("/api/v1/posts/1/likes"));
        Assert.False(pattern.Matches("/api/v1/posts/1/shares"));
    }
}
