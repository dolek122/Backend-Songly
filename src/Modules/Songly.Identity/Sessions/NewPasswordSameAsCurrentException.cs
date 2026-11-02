using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Sessions;

/// <summary>
/// Changing to the same password would change nothing while still logging the user out
/// everywhere — so it is rejected rather than silently accepted.
/// </summary>
internal sealed class NewPasswordSameAsCurrentException()
    : ApiException(StatusCodes.Status400BadRequest, "NEW_PASSWORD_SAME_AS_CURRENT", "Nowe hasło musi różnić się od obecnego");
