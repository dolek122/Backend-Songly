using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;
using Songly.Notification.Inbox;

namespace Songly.Notification.Http;

internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications");

        notifications.MapGet(string.Empty, (PageQuery page, ClaimsPrincipal user, NotificationService inbox, CancellationToken cancellationToken) =>
            inbox.ListAsync(user.GetUserId(), page, cancellationToken));

        notifications.MapGet("/unread-count", async (ClaimsPrincipal user, NotificationService inbox, CancellationToken cancellationToken) =>
            new UnreadCountResponse(await inbox.UnreadCountAsync(user.GetUserId(), cancellationToken)));

        notifications.MapPost("/{notificationId}/read", async Task<NoContent> (Guid notificationId, ClaimsPrincipal user, NotificationService inbox, CancellationToken cancellationToken) =>
        {
            await inbox.MarkReadAsync(notificationId, user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });

        notifications.MapPost("/read-all", async Task<NoContent> (ClaimsPrincipal user, NotificationService inbox, CancellationToken cancellationToken) =>
        {
            await inbox.MarkAllReadAsync(user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
