namespace Songly.NowPlaying.Stream;

/// <summary>The live stream of the friends' statuses (original ADR 0007).</summary>
internal sealed class StreamOptions
{
    public const string Section = "Songly:NowPlaying:Stream";

    /// <summary>A stream lives at most this long; the client reconnects (SSE does it by itself).</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>One client must not hold an instance with any number of connections.</summary>
    public int MaxPerUser { get; set; } = 3;

    public bool HeartbeatEnabled { get; set; } = true;

    /// <summary>
    /// Proxies and load balancers close a silent connection after tens of seconds, and presence is
    /// silent for hours. A comment every few seconds costs a few bytes and saves a loop of reconnects —
    /// and is the only way to notice sockets broken silently: a write fails only when one is tried.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long we remember having announced that a user is playing. Longer than the longest-living
    /// status (the manual one), or the marker would go before the status and nothing would tell that
    /// the status expired.
    /// </summary>
    public TimeSpan AnnouncedTtl { get; set; } = TimeSpan.FromHours(6);
}

/// <summary>The server-side poll of the provider (original ADR 0007, stage 1b).</summary>
internal sealed class PollOptions
{
    public const string Section = "Songly:NowPlaying:Poll";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>A short lock per observed user, so that with many instances one of them asks the provider in a cycle.</summary>
    public TimeSpan LockTtl { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// How long the provider is not asked after its 429 when it gave no <c>Retry-After</c>. A minute is
    /// two skipped runs — enough not to fall into "limit, penalty, limit", too little for presence to
    /// look broken.
    /// </summary>
    public TimeSpan ProviderBackoff { get; set; } = TimeSpan.FromMinutes(1);
}
