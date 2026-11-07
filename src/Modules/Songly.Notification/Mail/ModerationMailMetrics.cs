using System.Diagnostics.Metrics;

namespace Songly.Notification.Mail;

/// <summary>
/// <c>songly.moderation.mail.suppressed{kind}</c> (original ADR 0019): mails to moderators that the
/// interval (ADR 0018) silenced. A silenced mail looks to the moderator exactly like no cases at all, so
/// without the counter it is invisible; a steady series means the interval is too long or someone is
/// flooding.
/// </summary>
internal sealed class ModerationMailMetrics
{
    public const string MeterName = "Songly.Notification.Mail";

    private readonly Counter<long> _suppressed;

    public ModerationMailMetrics(IMeterFactory meterFactory) =>
        _suppressed = meterFactory.Create(MeterName).CreateCounter<long>("songly.moderation.mail.suppressed");

    public void Suppressed(string kind) => _suppressed.Add(1, new KeyValuePair<string, object?>("kind", kind));
}
