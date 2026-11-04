namespace Songly.Content.Domain;

/// <summary>
/// A closed list of reasons rather than free text: a queue has to be sortable and filterable, and
/// with a free field every report reads differently and the moderator reads them all one by one.
/// The details go to a separate, optional field.
/// </summary>
internal enum ReportReason
{
    Spam,
    Harassment,
    HateSpeech,
    IllegalContent,
    Other,
}

internal enum ReportStatus
{
    Pending,
    Dismissed,
}

/// <summary>
/// A post reported to its community's moderation. The table belongs to content, which owns the
/// posts and alone may reference them; who may see the queue is asked through the community
/// module's contract.
/// </summary>
/// <remarks>
/// There is no "upheld" status: deleting the post <b>is</b> upholding the report, and takes the
/// post's reports along (<c>ON DELETE CASCADE</c>) — without a post there is nothing to moderate.
/// The queue is therefore not an archive of decisions; <see cref="ReporterDecision"/> is what
/// remembers them.
/// </remarks>
internal sealed class PostReport
{
    // For EF Core.
    private PostReport()
    {
    }

    public PostReport(Guid postId, Guid reporterId, ReportReason reason, string? details, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        PostId = postId;
        ReporterId = reporterId;
        Reason = reason;
        Details = details;
        Status = ReportStatus.Pending;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid PostId { get; private set; }

    public Guid ReporterId { get; private set; }

    public ReportReason Reason { get; private set; }

    public string? Details { get; private set; }

    public ReportStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary><c>null</c> once the moderator who decided deleted their account — their decisions stay.</summary>
    public Guid? ReviewedBy { get; private set; }
}
