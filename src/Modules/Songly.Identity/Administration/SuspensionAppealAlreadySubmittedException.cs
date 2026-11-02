using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Administration;

/// <summary>
/// One appeal per suspension, pending or rejected alike — the structural anti-spam of a channel that
/// by nature leads from the sanctioned to the one who sanctioned (original ADR 0023).
/// </summary>
internal sealed class SuspensionAppealAlreadySubmittedException()
    : ApiException(StatusCodes.Status409Conflict, "SUSPENSION_APPEAL_ALREADY_SUBMITTED", "Odwołanie od tego zawieszenia zostało już złożone");
