namespace Songly.Notification.Mail;

internal sealed class ModerationMailOptions
{
    public const string Section = "Songly:Notification:ModerationMail";

    /// <summary>
    /// The interval between mails sent to <b>all moderators</b> of one community (original ADR 0018), per
    /// kind of case: reports, appeals, overturned bans — the messages someone outside the moderation sets
    /// off.
    /// </summary>
    /// <remarks>
    /// The per-reporter limit is not enough: it protects against one person, not ten acting together, and
    /// the flooded inbox is the same whoever fills it (the same logic as the password reset's resend
    /// interval). Only mail is silenced — the queue and the in-app notification stay, because the content is
    /// there and the moderator has to come there anyway.
    /// </remarks>
    public TimeSpan ModeratorInterval { get; set; } = TimeSpan.FromMinutes(15);
}
