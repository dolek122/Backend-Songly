namespace Songly.Common.RateLimiting;

/// <summary>
/// A path pattern in the original's syntax (Spring's <c>PathPattern</c>, as far as the limits use it): <c>*</c>
/// is one whole segment, <c>**</c> is any number of segments, anything else is a literal segment. Patterns, not
/// literal paths, so covering a path with an id in it is a change of configuration, not of code.
/// </summary>
internal sealed class PathPattern
{
    private readonly string[] _segments;

    public PathPattern(string pattern)
    {
        Pattern = pattern;
        _segments = Split(pattern);
    }

    /// <summary>The pattern as configured — the key of a window and a metric tag, never the request's own path.</summary>
    public string Pattern { get; }

    public bool Matches(string path) => Matches(_segments, Split(path));

    private static bool Matches(ReadOnlySpan<string> pattern, ReadOnlySpan<string> path)
    {
        if (pattern.IsEmpty)
        {
            return path.IsEmpty;
        }

        if (pattern[0] == "**")
        {
            // Zero segments, or one more and try again.
            return Matches(pattern[1..], path) || (!path.IsEmpty && Matches(pattern, path[1..]));
        }

        return !path.IsEmpty
            && (pattern[0] == "*" || string.Equals(pattern[0], path[0], StringComparison.Ordinal))
            && Matches(pattern[1..], path[1..]);
    }

    // A trailing slash is the same path, as in the original's matcher.
    private static string[] Split(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
