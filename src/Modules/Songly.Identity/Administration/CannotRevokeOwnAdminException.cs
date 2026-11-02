using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Administration;

/// <summary>
/// No administrator revokes their own role — and this is an <b>invariant</b>, not caution: only an
/// administrator can revoke, and nobody can revoke themselves, so at least one administrator always
/// remains. Getting admin access back never requires a manual <c>UPDATE</c> in the database.
/// </summary>
internal sealed class CannotRevokeOwnAdminException()
    : ApiException(StatusCodes.Status403Forbidden, "CANNOT_REVOKE_OWN_ADMIN", "Nie można odebrać uprawnień administratora samemu sobie");
