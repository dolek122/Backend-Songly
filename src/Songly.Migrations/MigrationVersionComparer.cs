using System.Globalization;
using System.Text.RegularExpressions;

namespace Songly.Migrations;

/// <summary>
/// Orders migration scripts by their Flyway version number, not by name.
/// </summary>
/// <remarks>
/// DbUp sorts by name, which puts <c>V10__</c> before <c>V2__</c> — the migrations would run out of
/// order and fail on tables that do not exist yet. A name without a version is rejected outright
/// instead of being sorted somewhere arbitrary.
/// </remarks>
internal sealed partial class MigrationVersionComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        return VersionOf(x).CompareTo(VersionOf(y));
    }

    internal static int VersionOf(string? scriptName)
    {
        var match = Version().Match(scriptName ?? string.Empty);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Migration script '{scriptName}' does not follow the V<number>__<description>.sql convention");
        }

        return int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"(?:^|\.)V(?<version>\d+)__[^.]+\.sql$")]
    private static partial Regex Version();
}
