using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.PasswordReset;

/// <summary>
/// One answer for every reason: unknown, expired or already used. A distinct "expired" would look
/// like better UX, but it confirms that such a token once existed — that the address has an account.
/// </summary>
internal sealed class InvalidPasswordResetTokenException()
    : ApiException(StatusCodes.Status400BadRequest, "INVALID_PASSWORD_RESET_TOKEN", "Link do resetu hasła jest nieprawidłowy lub wygasł");
