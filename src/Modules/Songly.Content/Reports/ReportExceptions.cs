using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Content.Reports;

/// <summary>
/// Only posts in communities can be reported: a report needs an addressee, and an ordinary post has
/// nobody today with the right to decide it — moderation is communal, not global (original ADR 0011).
/// </summary>
internal sealed class PostNotInCommunityException()
    : ApiException(StatusCodes.Status400BadRequest, "POST_NOT_IN_COMMUNITY", "Zgłaszać można wyłącznie posty opublikowane w społeczności");

internal sealed class CannotReportOwnPostException()
    : ApiException(StatusCodes.Status400BadRequest, "CANNOT_REPORT_OWN_POST", "Nie można zgłosić własnego posta");

internal sealed class PostAlreadyReportedException()
    : ApiException(StatusCodes.Status409Conflict, "POST_ALREADY_REPORTED", "Ten post został już przez Ciebie zgłoszony");

/// <summary>Also for a report from another community: 403 would confirm the id exists.</summary>
internal sealed class ReportNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "REPORT_NOT_FOUND", "Nie znaleziono zgłoszenia");

internal sealed class ReportRateLimitExceededException()
    : ApiException(StatusCodes.Status429TooManyRequests, "REPORT_RATE_LIMIT_EXCEEDED", "Zbyt wiele zgłoszeń w krótkim czasie, spróbuj ponownie później");

internal sealed class NotCommunityModeratorException()
    : ApiException(StatusCodes.Status403Forbidden, "NOT_COMMUNITY_MODERATOR", "Tylko moderator społeczności może wykonać tę akcję");
