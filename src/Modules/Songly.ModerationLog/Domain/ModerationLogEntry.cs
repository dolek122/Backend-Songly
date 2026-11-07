namespace Songly.ModerationLog.Domain;

/// <summary>
/// One entry of the moderation history (<c>moderation_log_entries</c>, original ADR 0024) — never changed once
/// written: a history that can be corrected is no history, and the entries come from decisions already made.
/// </summary>
/// <remarks>
/// <b>Deliberately without the reason for the sanction</b>, and without the text of appeals. The reason lives on
/// the sanction's row while it lasts, and is a moderator's free text that push channels never carry (original
/// ADR 0015). To keep it here it would have to travel in a domain event — one careless step from an e-mail,
/// the hole ADR 0015 closed. The log answers "whether and how many times", not "why".
/// </remarks>
internal sealed class ModerationLogEntry
{
    // For EF Core.
    private ModerationLogEntry()
    {
    }

    public ModerationLogEntry(
        ModerationAction action,
        Guid? communityId,
        Guid subjectId,
        Guid actorId,
        DateTimeOffset occurredAt,
        DateTimeOffset? expiresAt = null,
        int? reportCount = null)
    {
        Id = Guid.NewGuid();
        Action = action;
        CommunityId = communityId;
        SubjectId = subjectId;
        ActorId = actorId;
        ExpiresAt = expiresAt;
        ReportCount = reportCount;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    public ModerationAction Action { get; private set; }

    /// <summary><see langword="null"/> for platform decisions.</summary>
    public Guid? CommunityId { get; private set; }

    /// <summary>Whom the decision was about.</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>Who made it; <see langword="null"/> once their account is deleted — the decision outlives them.</summary>
    public Guid? ActorId { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public int? ReportCount { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
