using Songly.Profile.Profiles;

namespace Songly.Profile.Tests;

/// <summary>The LIKE escaping behind the user search (the search itself is tested against Postgres).</summary>
public sealed class UserSearchTests
{
    [Theory]
    [InlineData("meloman", "meloman")]
    [InlineData("100%", @"100\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData(@"back\slash", @"back\\slash")]
    [InlineData(@"\%_", @"\\\%\_")]
    public void Wildcards_and_the_escape_character_are_escaped(string query, string escaped) =>
        Assert.Equal(escaped, UserSearch.EscapeLike(query));
}
