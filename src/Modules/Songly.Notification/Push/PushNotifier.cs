using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Songly.Common.Push;
using Songly.Community.Contracts;
using Songly.Identity.Contracts;
using Songly.Notification.Domain;
using Songly.Profile.Contracts;
using Songly.Social.Contracts;

namespace Songly.Notification.Push;

/// <summary>
/// The third channel (original ADR 0040): a push to the phone next to the inbox and the mail, for every
/// <b>new</b> inbox entry — so it inherits the inbox's deduplication (ADR 0035) for free.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every type goes out, but the block filter works at the moment of sending.</b> The inbox hides blocked
/// users when read; mail was limited to moderation, because it cannot be hidden afterwards. Neither can a
/// push, but a push about moderation only would be dead for nearly everyone. The compromise: a block from
/// before the event silences the push, and a push sent before the block stays on the phone — the same state
/// as a notification read before the block. Moderation types get through, as in the inbox.
/// </para>
/// <para>
/// <b>The text: the type and the actor's name, nothing more.</b> No comment text, no reasons, and no names
/// for moderation decisions — the community stands there instead, because a ban is the community's, not the
/// moderator's. A phone's lock screen is the push channel in its purest form.
/// </para>
/// <para>
/// Sent in the request that made the notification, after its save, like the mail. One failing device does not
/// stop the others, and no failure reaches the caller: the inbox has the notification already.
/// </para>
/// </remarks>
internal sealed partial class PushNotifier
{
    public const string MeterName = "Songly.Notification.Push";

    private const string AppName = "Songly";

    private readonly IPushSender _sender;
    private readonly PushDeviceService _devices;
    private readonly ISocialGraph _socialGraph;
    private readonly ISuspendedAccounts _suspendedAccounts;
    private readonly IProfileDirectory _profiles;
    private readonly ICommunityDirectory _communities;
    private readonly ILogger<PushNotifier> _logger;
    private readonly Counter<long> _deliveries;

    public PushNotifier(
        IPushSender sender,
        PushDeviceService devices,
        ISocialGraph socialGraph,
        ISuspendedAccounts suspendedAccounts,
        IProfileDirectory profiles,
        ICommunityDirectory communities,
        IMeterFactory meterFactory,
        ILogger<PushNotifier> logger)
    {
        _sender = sender;
        _devices = devices;
        _socialGraph = socialGraph;
        _suspendedAccounts = suspendedAccounts;
        _profiles = profiles;
        _communities = communities;
        _logger = logger;

        // songly.push.deliveries{outcome}: sent, failed, invalid-token, skipped-blocked, skipped-suspended.
        _deliveries = meterFactory.Create(MeterName).CreateCounter<long>("songly.push.deliveries");
    }

    public async Task NotifyAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            await NotifyUncheckedAsync(notification, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPushFailed(notification.UserId, exception);
        }
    }

    private async Task NotifyUncheckedAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        var tokens = await _devices.TokensOfAsync(notification.UserId, cancellationToken);
        if (tokens.Count == 0)
        {
            return;
        }

        // A suspended account has no access to the inbox or to anything a push leads to (original ADR 0022),
        // and a suspension does not unpin the phones. Without this the lock screen kept showing the names of
        // people approaching them (ADR 0049).
        if ((await _suspendedAccounts.SuspendedUserIdsAsync(cancellationToken)).Contains(notification.UserId))
        {
            Outcome("skipped-suspended");
            return;
        }

        if (!NotificationTypes.Moderation.Contains(notification.Type)
            && (await _socialGraph.BlockedEitherWayIdsAsync(notification.UserId, cancellationToken)).Contains(notification.ActorId))
        {
            Outcome("skipped-blocked");
            return;
        }

        var (title, body) = await TextAsync(notification, cancellationToken);
        var data = new Dictionary<string, string>
        {
            ["type"] = JsonNamingPolicy.SnakeCaseUpper.ConvertName(notification.Type.ToString()),
            ["notificationId"] = notification.Id.ToString(),
            ["resourceId"] = notification.ResourceId.ToString(),
            ["title"] = title,
            ["body"] = body,
        };
        foreach (var token in tokens)
        {
            await DeliverAsync(notification.UserId, new PushMessage(token, title, body, data), cancellationToken);
        }
    }

    private async Task DeliverAsync(Guid recipientId, PushMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await _sender.SendAsync(message, cancellationToken);
            Outcome("sent");
        }
        catch (InvalidPushTokenException)
        {
            await _devices.ForgetAsync(message.Token, cancellationToken);
            Outcome("invalid-token");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Outcome("failed");
            LogDeliveryFailed(recipientId, exception);
        }
    }

    /// <summary>
    /// A <c>switch</c> without a default arm, on purpose: a new notification type does not compile until someone
    /// decides what its push says — and whether it carries the actor's name.
    /// </summary>
    private async Task<(string Title, string Body)> TextAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        // CS8524 asks for an arm for unnamed enum values, which never occur here. Turning it off keeps CS8509 —
        // a named type without an arm — as the compile error this switch exists for.
#pragma warning disable CS8524
        return notification.Type switch
        {
            NotificationType.PostLiked => (await ActorAsync(notification, cancellationToken), "Polubienie Twojego posta"),
            NotificationType.PostCommented => (await ActorAsync(notification, cancellationToken), "Nowy komentarz pod Twoim postem"),
            NotificationType.PostShared => (await ActorAsync(notification, cancellationToken), "Udostępnienie Twojego posta"),
            NotificationType.FriendRequestReceived => (await ActorAsync(notification, cancellationToken), "Zaproszenie do znajomych"),
            NotificationType.FriendRequestAccepted => (await ActorAsync(notification, cancellationToken), "Zaproszenie przyjęte — jesteście znajomymi"),
            NotificationType.CommunityMemberJoined => (await ActorAsync(notification, cancellationToken), "Dołączenie do " + await CommunityAsync(notification, cancellationToken)),
            NotificationType.CommunityModeratorGranted => (await CommunityAsync(notification, cancellationToken), "Masz teraz uprawnienia moderatora"),
            NotificationType.CommunityModeratorRevoked => (await CommunityAsync(notification, cancellationToken), "Nie masz już uprawnień moderatora"),
            NotificationType.CommunityBanned => (await CommunityAsync(notification, cancellationToken), "Moderacja zablokowała Ci pisanie w tej społeczności"),
            NotificationType.CommunityPostRemoved => (await CommunityAsync(notification, cancellationToken), "Moderacja usunęła Twój post"),

            // The resource is the post, not the community — a title without a name, but also without the moderator.
            NotificationType.CommunityCommentRemoved => (AppName, "Moderacja usunęła Twój komentarz"),
            NotificationType.BanAppealSubmitted => (await CommunityAsync(notification, cancellationToken), "Nowe odwołanie od bana do rozpatrzenia"),
            NotificationType.BanAppealAccepted => (await CommunityAsync(notification, cancellationToken), "Odwołanie uznane — ban zdjęty"),
            NotificationType.BanAppealRejected => (await CommunityAsync(notification, cancellationToken), "Odwołanie od bana odrzucone"),
            NotificationType.BanEscalationAccepted => (await CommunityAsync(notification, cancellationToken), "Administracja uchyliła ban"),
            NotificationType.BanEscalationRejected => (await CommunityAsync(notification, cancellationToken), "Administracja utrzymała ban"),

            // Here too the resource is the post — so no name, and no pointing at the reporter either.
            NotificationType.CommunityPostReported => (AppName, "Nowe zgłoszenie posta do rozpatrzenia"),
            NotificationType.PostReportDismissed => (AppName, "Moderacja nie uwzględniła Twojego zgłoszenia"),
        };
#pragma warning restore CS8524
    }

    private async Task<string> ActorAsync(UserNotification notification, CancellationToken cancellationToken) =>
        (await _profiles.DisplayNamesAsync([notification.ActorId], cancellationToken)).GetValueOrDefault(notification.ActorId) ?? AppName;

    private async Task<string> CommunityAsync(UserNotification notification, CancellationToken cancellationToken) =>
        await _communities.NameOfAsync(notification.ResourceId, cancellationToken) is { } name ? $"„{name}”" : AppName;

    private void Outcome(string outcome) => _deliveries.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send a push to user {RecipientId}")]
    private partial void LogDeliveryFailed(Guid recipientId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not prepare a push for user {RecipientId}")]
    private partial void LogPushFailed(Guid recipientId, Exception exception);
}
