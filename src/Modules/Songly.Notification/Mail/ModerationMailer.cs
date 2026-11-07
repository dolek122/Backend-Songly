using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Songly.Common.Events;
using Songly.Common.Mail;
using Songly.Common.Throttling;
using Songly.Community.Contracts;
using Songly.Content.Contracts;
using Songly.Identity.Contracts;
using Songly.Notification.Domain;

namespace Songly.Notification.Mail;

/// <summary>
/// The second channel for moderation decisions (original ADR 0013): e-mail next to the inbox. The inbox
/// closes the loop only for someone who opens the app — and ADR 0012 began with a report queue waiting for
/// a moderator who had no reason to look. Mail reaches the absent too.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only moderation types go out by mail, and not to save on mail.</b> The inbox hides notifications
/// from blocked users <i>when read</i>; a sent mail cannot be hidden afterwards, so mailing ordinary
/// activity would get round the block filter by the side door. Moderation types get through that filter
/// anyway, so for them — and only them — both channels say the same.
/// </para>
/// <para>
/// A handler of its own, not lines in the inbox's: the channels are independent, so a failed inbox write
/// does not stop the mail, nor the other way round — which is also what lets the mail preferences exist
/// (ADR 0020): turning mail off leaves the inbox. The limits on the events ("only the first report", "only
/// a new ban") sit at the publishers, so this channel inherits them.
/// </para>
/// </remarks>
internal sealed partial class ModerationMailer(
    IMailSender mailSender,
    IUserEmailDirectory emails,
    ICommunityDirectory communities,
    ICommunityMembership memberships,
    IAdminDirectory admins,
    ISuspendedAccounts suspendedAccounts,
    IIntervalGate gate,
    MailPreferenceService preferences,
    ModerationMailMetrics metrics,
    IOptions<ModerationMailOptions> options,
    ILogger<ModerationMailer> logger)
    : IDomainEventHandler<CommunityMemberBanned>,
      IDomainEventHandler<CommunityPostRemoved>,
      IDomainEventHandler<CommunityCommentRemoved>,
      IDomainEventHandler<PostReported>,
      IDomainEventHandler<CommunityBanAppealSubmitted>,
      IDomainEventHandler<CommunityBanAppealAccepted>,
      IDomainEventHandler<CommunityBanAppealRejected>,
      IDomainEventHandler<CommunityBanAppealEscalated>,
      IDomainEventHandler<CommunityBanEscalationAccepted>,
      IDomainEventHandler<CommunityBanEscalationRejected>,
      IDomainEventHandler<PostReportDismissed>
{
    /// <summary>The name of a community deleted between the decision and the mail — practically impossible in one request, but the text needs something.</summary>
    private const string DeletedCommunity = "(usunięta społeczność)";

    /// <summary>
    /// The term must reach the banned user: a temporary ban that looks permanent misses its point — "a week
    /// off" works only on someone who knows it is a week. The inbox does not carry it (a flat model), so the
    /// mail is the only channel with it.
    /// </summary>
    public Task HandleAsync(CommunityMemberBanned domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityMemberBanned), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync([domainEvent.UserId], domainEvent.ModeratorId, MailCategory.DecisionAboutMe, $"Songly — ban w społeczności „{community}”", $"""
                Cześć!

                Moderacja społeczności „{community}” w Songly zablokowała Ci możliwość
                publikowania i komentowania w niej. Nadal możesz czytać jej treści.

                {BanTerm(domainEvent)}

                Szczegóły znajdziesz w aplikacji, w skrzynce powiadomień.

                Zespół Songly
                """, cancellationToken);
        });

    public Task HandleAsync(CommunityPostRemoved domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityPostRemoved), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync([domainEvent.PostAuthorId], domainEvent.ModeratorId, MailCategory.DecisionAboutMe, $"Songly — Twój post w społeczności „{community}” został usunięty", $"""
                Cześć!

                Moderacja społeczności „{community}” w Songly usunęła Twój post.

                Szczegóły znajdziesz w aplikacji, w skrzynce powiadomień.

                Zespół Songly
                """, cancellationToken);
        });

    /// <summary>The same category and shape as a removed post — without the comment's text, which is gone anyway, and without the moderator.</summary>
    public Task HandleAsync(CommunityCommentRemoved domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityCommentRemoved), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync([domainEvent.CommentAuthorId], domainEvent.ModeratorId, MailCategory.DecisionAboutMe, $"Songly — Twój komentarz w społeczności „{community}” został usunięty", $"""
                Cześć!

                Moderacja społeczności „{community}” w Songly usunęła Twój komentarz.

                Szczegóły znajdziesz w aplikacji, w skrzynce powiadomień.

                Zespół Songly
                """, cancellationToken);
        });

    /// <summary>
    /// The mail this channel exists for: a report in a community its moderator does not happen to visit lay
    /// in the queue indefinitely, and the in-app notification is seen only by someone who opens the app.
    /// </summary>
    public Task HandleAsync(PostReported domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(PostReported), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendToModeratorsAsync(domainEvent.CommunityId, "report", domainEvent.ReporterId, $"Songly — nowe zgłoszenie w społeczności „{community}”", $"""
                Cześć!

                W społeczności „{community}” w Songly pojawiło się zgłoszenie posta.
                Zajrzyj do kolejki zgłoszeń w aplikacji, żeby je rozpatrzyć.

                Kolejne zgłoszenia tego samego posta nie wygenerują następnych
                wiadomości — ich liczbę zobaczysz przy poście w kolejce.

                Zespół Songly
                """, cancellationToken);
        });

    /// <summary>
    /// Without the appeal's text, on purpose, like the ban's reason (original ADR 0015): text from a user does
    /// not travel a push channel. The mail says there is a reason to look; the text waits on the list of bans.
    /// </summary>
    public Task HandleAsync(CommunityBanAppealSubmitted domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityBanAppealSubmitted), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendToModeratorsAsync(domainEvent.CommunityId, "appeal", domainEvent.UserId, $"Songly — odwołanie od bana w społeczności „{community}”", $"""
                Cześć!

                W społeczności „{community}” w Songly ktoś odwołał się od bana.
                Treść odwołania znajdziesz w aplikacji, na liście banów.

                Odwołanie jest jedno na ban, więc kolejnych wiadomości w tej
                sprawie nie będzie.

                Zespół Songly
                """, cancellationToken);
        });

    public Task HandleAsync(CommunityBanAppealAccepted domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityBanAppealAccepted), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync([domainEvent.UserId], domainEvent.ModeratorId, MailCategory.DecisionAboutMe, $"Songly — Twoje odwołanie w społeczności „{community}” zostało uznane", $"""
                Cześć!

                Moderacja społeczności „{community}” w Songly uznała Twoje odwołanie.
                Ban został zdjęty — możesz wrócić do społeczności.

                Zespół Songly
                """, cancellationToken);
        });

    public Task HandleAsync(CommunityBanAppealRejected domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityBanAppealRejected), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync([domainEvent.UserId], domainEvent.ModeratorId, MailCategory.DecisionAboutMe, $"Songly — Twoje odwołanie w społeczności „{community}” zostało odrzucone", $"""
                Cześć!

                Moderacja społeczności „{community}” w Songly rozpatrzyła Twoje odwołanie
                i uznała, że ban pozostaje w mocy. Ta decyzja jest ostateczna dla
                tego bana.

                Zespół Songly
                """, cancellationToken);
        });

    /// <summary>
    /// The second instance (original ADR 0028), to the <b>platform's administration</b>: there is no queue in
    /// the app it would look into by itself. Without the appeal's text — it waits in the queue of cases.
    /// </summary>
    public Task HandleAsync(CommunityBanAppealEscalated domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityBanAppealEscalated), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync(await admins.AdminIdsAsync(cancellationToken), domainEvent.UserId, MailCategory.ModerationQueue, "Songly — odwołanie przekazane do administracji", $"""
                Cześć!

                Zbanowany w społeczności „{community}” przekazał swoje odwołanie do
                administracji platformy, bo moderacja społeczności je odrzuciła.
                Treść znajdziesz w aplikacji, w kolejce spraw drugiej instancji.

                Zespół Songly
                """, cancellationToken);
        });

    /// <summary>
    /// To the appellant, and to the community's moderators: they have to learn their decision was overturned,
    /// or the banned user comes back without a word and it looks like a failure — the argument ADR 0012
    /// began with.
    /// </summary>
    public Task HandleAsync(CommunityBanEscalationAccepted domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityBanEscalationAccepted), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            var subject = $"Songly — administracja uchyliła ban w społeczności „{community}”";
            await SendAsync([domainEvent.UserId], domainEvent.AdminId, MailCategory.DecisionAboutMe, subject, $"""
                Cześć!

                Administracja Songly rozpatrzyła Twoje odwołanie i uchyliła ban w
                społeczności „{community}”. Możesz do niej wrócić.

                Zespół Songly
                """, cancellationToken);
            await SendToModeratorsAsync(domainEvent.CommunityId, "escalation", domainEvent.UserId, subject, $"""
                Cześć!

                Administracja Songly rozpatrzyła odwołanie od banu w społeczności
                „{community}” i uchyliła tę decyzję, więc zbanowana osoba wraca do
                społeczności.

                Szczegóły sprawy znajdziesz w historii moderacji w aplikacji.

                Zespół Songly
                """, cancellationToken);
        });

    public Task HandleAsync(CommunityBanEscalationRejected domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(CommunityBanEscalationRejected), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync([domainEvent.UserId], domainEvent.AdminId, MailCategory.DecisionAboutMe, $"Songly — administracja utrzymała ban w społeczności „{community}”", $"""
                Cześć!

                Administracja Songly rozpatrzyła Twoje odwołanie od banu w
                społeczności „{community}” i utrzymała decyzję moderacji. To rozstrzygnięcie
                jest ostateczne dla tego banu.

                Zespół Songly
                """, cancellationToken);
        });

    public Task HandleAsync(PostReportDismissed domainEvent, CancellationToken cancellationToken) =>
        SafelyAsync(nameof(PostReportDismissed), async () =>
        {
            var community = await CommunityNameAsync(domainEvent.CommunityId, cancellationToken);
            await SendAsync([domainEvent.ReporterId], domainEvent.ModeratorId, MailCategory.DecisionAboutMe, "Songly — Twoje zgłoszenie zostało rozpatrzone", $"""
                Cześć!

                Moderacja społeczności „{community}” w Songly obejrzała zgłoszony przez Ciebie
                post i uznała, że może zostać.

                Dziękujemy za zgłoszenie — to dzięki nim moderacja wie, czemu się
                przyjrzeć, także wtedy, gdy decyzja jest inna, niż się spodziewasz.

                Zespół Songly
                """, cancellationToken);
        });

    private static string BanTerm(CommunityMemberBanned domainEvent)
    {
        if (domainEvent.ExpiresAt is not { } expiresAt)
        {
            return "Ban jest bezterminowy.";
        }

        var days = (expiresAt - domainEvent.OccurredAt).Days;
        return $"Ban jest czasowy — wygasa za {days} {(days == 1 ? "dzień" : "dni")} i wtedy możesz wrócić.";
    }

    /// <summary>
    /// To <b>all moderators</b> of a community, silenced by an interval per community and kind of case
    /// (original ADR 0018).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the mails someone outside the moderation sets off — so the only ones that can flood
    /// someone else's inbox. A silenced mail is <b>dropped</b>, not queued, and that is fine exactly because
    /// it never carried content: it says "there is a reason to look", and the queue shows everything that
    /// came in the meantime. The in-app notifications stay untouched. Fail-open, like every interval gate:
    /// when Redis does not answer, the mail goes out.
    /// </para>
    /// <para>
    /// A suspended moderator cannot handle the case the mail calls them to (original ADR 0022, 0049) — calls
    /// to the queue go only to those who can open it.
    /// </para>
    /// </remarks>
    private async Task SendToModeratorsAsync(Guid communityId, string kind, Guid actorId, string subject, string body, CancellationToken cancellationToken)
    {
        if (!await gate.TryPassAsync($"moderation-mail:{kind}:{communityId}", options.Value.ModeratorInterval))
        {
            metrics.Suppressed(kind);
            LogSuppressed(communityId, kind);
            return;
        }

        var suspended = await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken);
        var moderators = (await memberships.ModeratorIdsAsync(communityId, cancellationToken)).Where(id => !suspended.Contains(id)).ToList();
        await SendAsync(moderators, actorId, MailCategory.ModerationQueue, subject, body, cancellationToken);
    }

    /// <summary>
    /// To many recipients at once. Whoever made the decision drops out — nobody notifies themselves, the rule
    /// of the inbox. The preferences (ADR 0020) filter per recipient; the interval of ADR 0018 works before
    /// and independently, its window belongs to the community. Addresses come in one query; an id without an
    /// account is simply not there.
    /// </summary>
    /// <remarks>
    /// A delivery failure is caught <b>per recipient</b> — one undeliverable address must not stop the mail to
    /// the other moderators. The inbox has the notification already, so a lost mail degrades the channel,
    /// not the information. The log has the user's id only, never the address.
    /// </remarks>
    private async Task SendAsync(IEnumerable<Guid> recipientIds, Guid? actorId, MailCategory category, string subject, string body, CancellationToken cancellationToken)
    {
        var recipients = await preferences.AllowedRecipientsAsync([.. recipientIds.Where(id => id != actorId).Distinct()], category, cancellationToken);
        foreach (var (recipientId, address) in await emails.EmailsOfAsync(recipients, cancellationToken))
        {
            try
            {
                await mailSender.SendAsync(new OutgoingEmail(address, subject, body), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDeliveryFailed(recipientId, exception);
            }
        }
    }

    private async Task<string> CommunityNameAsync(Guid communityId, CancellationToken cancellationToken) =>
        await communities.NameOfAsync(communityId, cancellationToken) ?? DeletedCommunity;

    /// <summary>The decision is committed already; a mail that cannot even be prepared must not turn it into a 500 (ADR 0012).</summary>
    private async Task SafelyAsync(string eventName, Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogMailFailed(eventName, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mail to the moderators of {CommunityId} ({Kind}) silenced by the interval")]
    private partial void LogSuppressed(Guid communityId, string kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send a moderation mail to user {RecipientId}")]
    private partial void LogDeliveryFailed(Guid recipientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not prepare the mail for {EventName}")]
    private partial void LogMailFailed(string eventName, Exception exception);
}
