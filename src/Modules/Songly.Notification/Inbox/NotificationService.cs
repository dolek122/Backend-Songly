using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Songly.Common.Paging;
using Songly.Notification.Domain;
using Songly.Notification.Http;
using Songly.Notification.Persistence;
using Songly.Profile.Contracts;
using Songly.Social.Contracts;

namespace Songly.Notification.Inbox;

/// <summary>The in-app inbox: recording notifications, reading them, marking them read.</summary>
internal sealed partial class NotificationService(
    NotificationDbContext db,
    IProfileDirectory profiles,
    ISocialGraph socialGraph,
    TimeProvider time,
    ILogger<NotificationService> logger)
{
    /// <summary>
    /// Records a notification, unless it is the recipient's own activity (liking one's own post) or a
    /// state undone and restored that the recipient was told about already (original ADR 0035).
    /// </summary>
    /// <remarks>
    /// Called from handlers of events published <b>after</b> the publisher's save: the like is a
    /// committed fact, and a notification that could not be written must not turn it into a 500. So a
    /// failure is logged and swallowed — the original's <c>AFTER_COMMIT</c> listener with a transaction
    /// of its own (<c>REQUIRES_NEW</c>) behaved the same; see this repository's ADR 0012.
    /// </remarks>
    public async Task RecordAsync(Guid recipientId, Guid actorId, NotificationType type, Guid resourceId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (recipientId == actorId)
        {
            return;
        }

        try
        {
            await RecordUncheckedAsync(recipientId, actorId, type, resourceId, occurredAt, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // What failed to save must not be saved again by the next notification of the same request.
            db.ChangeTracker.Clear();
            LogRecordFailed(type, recipientId, exception);
        }
    }

    /// <summary>
    /// Notifications from blocked users are <b>hidden when read, not deleted when blocking</b>. Deleting
    /// would be irreversible: after an unblock the inbox would come back with a hole, and blocks are
    /// sometimes temporary. With a filter on reading, unblocking simply brings back what was there —
    /// read state included.
    /// </summary>
    public async Task<PagedResponse<NotificationResponse>> ListAsync(Guid userId, PageQuery page, CancellationToken cancellationToken)
    {
        var rows = await (await VisibleAsync(userId, cancellationToken))
            .OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .ToPageAsync(page, notification => notification, cancellationToken);

        // Without a name or an avatar for moderation decisions (original ADR 0045). Checked per row, not
        // only when building the maps: the same moderator can have an ordinary notification (a like) on
        // the same page, and then their name is in the map.
        var actorIds = rows.Content
            .Where(n => !NotificationTypes.DecidedByModeration.Contains(n.Type))
            .Select(n => n.ActorId)
            .ToHashSet();
        var names = await profiles.DisplayNamesAsync(actorIds, cancellationToken);
        var avatars = await profiles.AvatarUrlsAsync(actorIds, cancellationToken);
        return new PagedResponse<NotificationResponse>(
            [
                .. rows.Content.Select(n =>
                {
                    var anonymous = NotificationTypes.DecidedByModeration.Contains(n.Type);
                    return new NotificationResponse(
                        n.Id,
                        ToDto(n.Type),
                        n.ActorId,
                        anonymous ? null : names.GetValueOrDefault(n.ActorId),
                        anonymous ? null : avatars.GetValueOrDefault(n.ActorId),
                        n.ResourceId,
                        n.CreatedAt,
                        n.ReadAt);
                }),
            ],
            rows.Page);
    }

    /// <summary>
    /// Filtered exactly like the list — otherwise the badge would say "3 new" above an inbox without a
    /// single visible new notification, and nothing would bring it down.
    /// </summary>
    public async Task<long> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        await (await VisibleAsync(userId, cancellationToken)).LongCountAsync(n => n.ReadAt == null, cancellationToken);

    /// <summary>Someone else's notification reads as one that does not exist.</summary>
    public async Task MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken)
            ?? throw new NotificationNotFoundException();
        notification.MarkRead(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Marks read only what the user actually sees. Reaching the hidden ones too, they would come back
    /// read after an unblock although nobody ever saw them — and the block filter is meant to be a view,
    /// not a quiet change of state.
    /// </summary>
    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await (await VisibleAsync(userId, cancellationToken))
            .Where(n => n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    private async Task RecordUncheckedAsync(Guid recipientId, Guid actorId, NotificationType type, Guid resourceId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (NotificationTypes.OncePerResource.Contains(type)
            && await db.Notifications.AnyAsync(n => n.UserId == recipientId && n.Type == type && n.ActorId == actorId && n.ResourceId == resourceId, cancellationToken))
        {
            return;
        }

        if (NotificationTypes.CollapsedWhileUnread.Contains(type)
            && await db.Notifications.FirstOrDefaultAsync(n => n.UserId == recipientId && n.Type == type && n.ActorId == actorId && n.ReadAt == null, cancellationToken) is { } unread)
        {
            unread.Refresh(resourceId, occurredAt);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        db.Notifications.Add(new UserNotification(recipientId, type, actorId, resourceId, occurredAt));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The user's notifications without those from users blocked either way — <b>except</b> moderation
    /// decisions, which always get through (<see cref="NotificationTypes.Moderation"/>). In the query, not
    /// after reading the page: filtering afterwards would break paging, as in the content module.
    /// </summary>
    private async Task<IQueryable<UserNotification>> VisibleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var blocked = await socialGraph.BlockedEitherWayIdsAsync(userId, cancellationToken);
        var alwaysVisible = NotificationTypes.Moderation.ToList();
        return db.Notifications.Where(n => n.UserId == userId && (!blocked.Contains(n.ActorId) || alwaysVisible.Contains(n.Type)));
    }

    private static NotificationTypeDto ToDto(NotificationType type) => type switch
    {
        NotificationType.PostLiked => NotificationTypeDto.PostLiked,
        NotificationType.PostCommented => NotificationTypeDto.PostCommented,
        NotificationType.PostShared => NotificationTypeDto.PostShared,
        NotificationType.FriendRequestReceived => NotificationTypeDto.FriendRequestReceived,
        NotificationType.FriendRequestAccepted => NotificationTypeDto.FriendRequestAccepted,
        NotificationType.CommunityMemberJoined => NotificationTypeDto.CommunityMemberJoined,
        NotificationType.CommunityBanned => NotificationTypeDto.CommunityBanned,
        NotificationType.CommunityPostRemoved => NotificationTypeDto.CommunityPostRemoved,
        NotificationType.CommunityCommentRemoved => NotificationTypeDto.CommunityCommentRemoved,
        NotificationType.CommunityPostReported => NotificationTypeDto.CommunityPostReported,
        NotificationType.BanEscalationAccepted => NotificationTypeDto.BanEscalationAccepted,
        NotificationType.BanEscalationRejected => NotificationTypeDto.BanEscalationRejected,
        NotificationType.PostReportDismissed => NotificationTypeDto.PostReportDismissed,
        NotificationType.BanAppealSubmitted => NotificationTypeDto.BanAppealSubmitted,
        NotificationType.BanAppealAccepted => NotificationTypeDto.BanAppealAccepted,
        NotificationType.BanAppealRejected => NotificationTypeDto.BanAppealRejected,
        NotificationType.CommunityModeratorGranted => NotificationTypeDto.CommunityModeratorGranted,
        NotificationType.CommunityModeratorRevoked => NotificationTypeDto.CommunityModeratorRevoked,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not record a {Type} notification for {RecipientId}")]
    private partial void LogRecordFailed(NotificationType type, Guid recipientId, Exception exception);
}
