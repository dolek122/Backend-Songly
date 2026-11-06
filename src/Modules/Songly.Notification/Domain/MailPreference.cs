namespace Songly.Notification.Domain;

/// <summary>
/// A category of e-mail as the preferences see it (original ADR 0020). Deliberately <b>two</b>, not a
/// switch per notification type: types × channels is a settings screen nobody reads, and every new type
/// would need a decision about its default. These two match the two roles in which someone gets mail.
/// </summary>
internal enum MailCategory
{
    /// <summary>A ban, my removed post or comment, the answer to my report or appeal.</summary>
    DecisionAboutMe,

    /// <summary>A call to moderation work: a new report, a new appeal.</summary>
    ModerationQueue,
}

/// <summary>
/// One user's e-mail preferences (<c>notification_mail_preferences</c>, original ADR 0020). The row is
/// written at the first change only — no row means both categories on, so nothing is created at
/// registration (the same pattern as the now-playing preference).
/// </summary>
internal sealed class MailPreference
{
    // For EF Core.
    private MailPreference()
    {
    }

    public MailPreference(Guid userId, bool decisionsAboutMe, bool moderationQueue, DateTimeOffset now)
    {
        UserId = userId;
        Update(decisionsAboutMe, moderationQueue, now);
    }

    public Guid UserId { get; private set; }

    public bool DecisionsAboutMe { get; private set; }

    public bool ModerationQueue { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool Allows(MailCategory category) => category == MailCategory.DecisionAboutMe ? DecisionsAboutMe : ModerationQueue;

    public void Update(bool decisionsAboutMe, bool moderationQueue, DateTimeOffset now)
    {
        DecisionsAboutMe = decisionsAboutMe;
        ModerationQueue = moderationQueue;
        UpdatedAt = now;
    }
}
