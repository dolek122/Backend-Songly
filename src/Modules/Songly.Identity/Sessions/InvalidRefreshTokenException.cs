using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Sessions;

internal class InvalidRefreshTokenException()
    : ApiException(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Refresh token jest nieprawidłowy lub wygasł");
