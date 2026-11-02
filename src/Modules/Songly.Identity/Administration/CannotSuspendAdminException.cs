using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Administration;

/// <summary>
/// An administrator does not suspend another one — otherwise two of them could switch each other
/// off, and whoever clicked first would win. Suspending an admin takes two deliberate steps:
/// revoke the role, then suspend.
/// </summary>
internal sealed class CannotSuspendAdminException()
    : ApiException(StatusCodes.Status403Forbidden, "CANNOT_SUSPEND_ADMIN", "Najpierw odbierz uprawnienia administratora, potem zawieś konto");
