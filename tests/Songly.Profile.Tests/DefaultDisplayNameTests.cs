using System.Text.RegularExpressions;
using Songly.Profile.Profiles;

namespace Songly.Profile.Tests;

public sealed partial class DefaultDisplayNameTests
{
    /// <summary>Six digits, zero-padded: "Użytkownik 000042", never "Użytkownik 42".</summary>
    [Fact]
    public void Is_the_prefix_and_six_digits()
    {
        for (var i = 0; i < 1000; i++)
        {
            Assert.Matches(Pattern(), DefaultDisplayName.Generate());
        }
    }

    [GeneratedRegex(@"^Użytkownik \d{6}$")]
    private static partial Regex Pattern();
}
