using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Administration;

/// <summary>
/// The account does not exist. This 404 is <b>not</b> an oracle for the existence of accounts: the
/// endpoints require the administrator role, and an administrator sees the whole service anyway.
/// </summary>
internal sealed class AdminUserNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "USER_NOT_FOUND", "Nie ma konta o podanym identyfikatorze");
