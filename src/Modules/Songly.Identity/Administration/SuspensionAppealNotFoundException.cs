using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Identity.Administration;

/// <summary>There is no pending appeal to reject.</summary>
internal sealed class SuspensionAppealNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "SUSPENSION_APPEAL_NOT_FOUND", "Brak oczekującego odwołania");
