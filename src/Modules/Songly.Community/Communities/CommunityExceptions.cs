using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Community.Communities;

internal sealed class CommunityNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "COMMUNITY_NOT_FOUND", "Społeczność nie została znaleziona");

internal sealed class CommunityNameTakenException()
    : ApiException(StatusCodes.Status409Conflict, "COMMUNITY_NAME_TAKEN", "Społeczność o tej nazwie już istnieje");

internal sealed class NotCommunityOwnerException()
    : ApiException(StatusCodes.Status403Forbidden, "NOT_COMMUNITY_OWNER", "Tylko właściciel społeczności może wykonać tę akcję");

internal sealed class OwnerCannotLeaveException()
    : ApiException(StatusCodes.Status409Conflict, "OWNER_CANNOT_LEAVE", "Właściciel nie może opuścić społeczności — usuń ją albo przekaż własność");

internal sealed class CannotAssignOwnerRoleException()
    : ApiException(StatusCodes.Status400BadRequest, "CANNOT_ASSIGN_OWNER_ROLE", "Roli właściciela nie nadaje się przez zmianę roli członka");

internal sealed class CannotModerateOwnerException()
    : ApiException(StatusCodes.Status403Forbidden, "CANNOT_MODERATE_OWNER", "Właściciela społeczności nie da się moderować");

internal sealed class NotCommunityMemberToModerateException()
    : ApiException(StatusCodes.Status404NotFound, "NOT_COMMUNITY_MEMBER", "Ten użytkownik nie należy do społeczności");
