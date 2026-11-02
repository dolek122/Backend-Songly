using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Administration;

/// <summary>Only an <b>active</b> suspension can be appealed — an expired one is no suspension.</summary>
internal sealed class NoSuspensionToAppealException()
    : ApiException(StatusCodes.Status409Conflict, "NO_SUSPENSION_TO_APPEAL", "Twoje konto nie jest zawieszone");
