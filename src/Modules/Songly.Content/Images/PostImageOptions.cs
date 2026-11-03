namespace Songly.Content.Images;

/// <summary>Images uploaded and not attached to a post yet (original ADR 0048).</summary>
internal sealed class PostImageOptions
{
    public const string Section = "Songly:Content:PostImage";

    /// <summary>
    /// How many images may wait for a post at once. One more upload evicts the oldest instead of
    /// failing: the mobile app uploads an image as soon as it is picked, so a few abandoned ones are
    /// ordinary browsing of the gallery, not abuse — and refusing would block a person for a day.
    /// </summary>
    public int MaxPending { get; set; } = 10;

    /// <summary>After how long an unattached image counts as abandoned.</summary>
    public TimeSpan PendingTtl { get; set; } = TimeSpan.FromHours(24);

    public int CleanupBatchSize { get; set; } = 500;

    public bool CleanupEnabled { get; set; } = true;

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
