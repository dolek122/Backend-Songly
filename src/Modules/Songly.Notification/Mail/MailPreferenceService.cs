using Microsoft.EntityFrameworkCore;
using Songly.Notification.Domain;
using Songly.Notification.Http;
using Songly.Notification.Persistence;

namespace Songly.Notification.Mail;

/// <summary>
/// The e-mail preferences (original ADR 0020). They govern <b>mail only</b>: the inbox stays complete,
/// because a moderation decision cannot be declined (ADR 0012), and the appeal path (ADR 0017) would mean
/// nothing if one could avoid learning of one's own ban.
/// </summary>
internal sealed class MailPreferenceService(NotificationDbContext db, TimeProvider time)
{
    /// <summary>No row reads as the default "both on", without writing anything on a read.</summary>
    public async Task<MailPreferencesResponse> SettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.MailPreferences.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new MailPreferencesResponse(p.DecisionsAboutMe, p.ModerationQueue))
            .SingleOrDefaultAsync(cancellationToken)
        ?? new MailPreferencesResponse(true, true);

    public async Task UpdateAsync(Guid userId, bool decisionsAboutMe, bool moderationQueue, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (await db.MailPreferences.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken) is { } preference)
        {
            preference.Update(decisionsAboutMe, moderationQueue, now);
        }
        else
        {
            db.MailPreferences.Add(new MailPreference(userId, decisionsAboutMe, moderationQueue, now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The recipients who want this category. No row is consent — the setting is an opt-out, because
    /// these mails follow from using the service, they are not marketing (original ADR 0013).
    /// </summary>
    /// <remarks>
    /// Filtered <b>per recipient</b>, not "send or not": a report mail goes to all moderators at once, so one
    /// who opted out must not silence it for the others. One query for all of them.
    /// </remarks>
    public async Task<IReadOnlyCollection<Guid>> AllowedRecipientsAsync(IReadOnlyCollection<Guid> userIds, MailCategory category, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var preferences = await db.MailPreferences.AsNoTracking().Where(p => userIds.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, cancellationToken);
        return [.. userIds.Where(id => !preferences.TryGetValue(id, out var preference) || preference.Allows(category))];
    }
}
