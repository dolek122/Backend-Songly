using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Registration;

/// <summary>
/// One answer for every reason: the token is unknown, expired, already used, or its address got an
/// account in the meantime. Telling them apart would rebuild the oracle this whole flow removes —
/// "this address already has an account" is exactly what registration stopped telling anyone.
/// </summary>
internal sealed class InvalidVerificationTokenException()
    : ApiException(StatusCodes.Status400BadRequest, "INVALID_VERIFICATION_TOKEN", "Link potwierdzający jest nieprawidłowy lub wygasł");
