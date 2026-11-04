using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Notification.Inbox;

internal sealed class NotificationNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "NOTIFICATION_NOT_FOUND", "Powiadomienie nie zostało znalezione");
