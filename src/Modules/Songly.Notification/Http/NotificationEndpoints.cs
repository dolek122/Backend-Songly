using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Songly.Common.Paging;
using Songly.Common.Web;
using Songly.Notification.Inbox;
using Songly.Notification.Mail;
using Songly.Notification.Push;

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

        // Mail only (original ADR 0020) — hence "mail-preferences", not "preferences": the inbox cannot be
        // turned off, and the endpoint's name says so.
        notifications.MapGet("/mail-preferences", (ClaimsPrincipal user, MailPreferenceService preferences, CancellationToken cancellationToken) =>
            preferences.SettingsAsync(user.GetUserId(), cancellationToken));

        notifications.MapPut("/mail-preferences", async Task<NoContent> (UpdateMailPreferencesRequest request, ClaimsPrincipal user, MailPreferenceService preferences, CancellationToken cancellationToken) =>
        {
            await preferences.UpdateAsync(user.GetUserId(), request.DecisionsAboutMe!.Value, request.ModerationQueue!.Value, cancellationToken);
            return TypedResults.NoContent();
        });

        // Idempotent — the app calls it at every start and whenever FCM replaces the token (original ADR 0040).
        notifications.MapPut("/push-devices", async Task<NoContent> (RegisterPushDeviceRequest request, ClaimsPrincipal user, PushDeviceService devices, CancellationToken cancellationToken) =>
        {
            await devices.RegisterAsync(user.GetUserId(), request.Token!, request.Platform!.Value, cancellationToken);
            return TypedResults.NoContent();
        });

        // Before signing out. POST .../removal, not DELETE: the token travels in the body, and a DELETE with a
        // body gets lost by proxies. Someone else's or an unknown token is a 204 too.
        notifications.MapPost("/push-devices/removal", async Task<NoContent> (RemovePushDeviceRequest request, ClaimsPrincipal user, PushDeviceService devices, CancellationToken cancellationToken) =>
        {
            await devices.UnregisterAsync(user.GetUserId(), request.Token!, cancellationToken);
            return TypedResults.NoContent();
        });

        notifications.MapPost("/read-all", async Task<NoContent> (ClaimsPrincipal user, NotificationService inbox, CancellationToken cancellationToken) =>
        {
            await inbox.MarkAllReadAsync(user.GetUserId(), cancellationToken);
            return TypedResults.NoContent();
        });
    }
}
