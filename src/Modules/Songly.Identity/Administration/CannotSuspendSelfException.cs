using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Administration;

/// <summary>
/// An administrator suspending themselves would lose access to the endpoint that undoes it, and
/// getting back in would take a manual <c>UPDATE</c> — exactly what original ADR 0021 removed.
/// </summary>
internal sealed class CannotSuspendSelfException()
    : ApiException(StatusCodes.Status403Forbidden, "CANNOT_SUSPEND_SELF", "Nie można zawiesić własnego konta");
