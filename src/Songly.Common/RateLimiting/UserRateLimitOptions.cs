namespace Songly.Common.RateLimiting;

/// <summary>
/// The limits per user on the paths behind a token (<c>Songly:Security:UserRateLimit</c>, original ADR 0034).
/// A rule is a <b>named group</b> of paths and methods with one counter, not a limit per path: what counts is
/// how much of a <i>kind</i> of thing one person does, not through which endpoint — a like, a comment and a
/// friend request all end as a notification in someone else's inbox, so spreading the flood over three paths
/// must not give three budgets.
/// </summary>
/// <remarks>
/// A request counts towards <b>every</b> rule it matches and is refused when it exceeds any of them. Configuring
/// <see cref="Rules"/> replaces the whole list, it does not add to it.
/// </remarks>
internal sealed class UserRateLimitOptions
{
    public const string Section = "Songly:Security:UserRateLimit";

    /// <summary>Writes only — reads are deliberately not limited: the harm defended against is volume at someone else's.</summary>
    public static readonly IReadOnlyList<string> WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public static IReadOnlyList<UserRateLimitRule> DefaultRules =>
    [
        // A budget for every write. The whole prefix, not a list of endpoints: a new endpoint is limited by
        // default, not once someone remembers to add it (the same principle as /api/v1/admin). Out of reach by
        // hand — it is there to stop a script, not a person.
        new() { Name = "writes", Methods = [.. WriteMethods], Paths = ["/api/**"], Limit = 60, Window = TimeSpan.FromMinutes(1) },

        // The paths that end in a notification for SOMEONE ELSE. Only the reaching-out direction (PUT of a like,
        // not its DELETE): withdrawing one's own trace must not depend on a limit, as it does not depend on a
        // block. A longer window, because the harm is the volume at one victim, not a momentary burst.
        new()
        {
            Name = "interactions",
            Methods = ["PUT", "POST"],
            Paths = ["/api/v1/posts/*/likes", "/api/v1/posts/*/shares", "/api/v1/posts/*/comments", "/api/v1/friends/requests"],
            Limit = 40,
            Window = TimeSpan.FromMinutes(10),
        },
    ];

    public bool Enabled { get; set; } = true;

    /// <summary>Empty means <see cref="DefaultRules"/>.</summary>
    public List<UserRateLimitRule> Rules { get; set; } = [];
}

internal sealed class UserRateLimitRule
{
    /// <summary>
    /// Part of the Redis key and the metric's tag — so a constant from the configuration, never anything that
    /// comes from the request (the tag's cardinality).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>Empty means <see cref="UserRateLimitOptions.WriteMethods"/> — not a default list the binder would append to.</summary>
    public List<string> Methods { get; set; } = [];

    public List<string> Paths { get; set; } = [];

    public int Limit { get; set; }

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
