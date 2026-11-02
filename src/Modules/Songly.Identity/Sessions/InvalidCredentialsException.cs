using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Sessions;

/// <summary>One answer for an unknown account and a wrong password — the response must not reveal which.</summary>
internal sealed class InvalidCredentialsException()
    : ApiException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Nieprawidłowy e-mail lub hasło");
