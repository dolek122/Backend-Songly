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

internal sealed class NotCommunityModeratorException()
    : ApiException(StatusCodes.Status403Forbidden, "NOT_COMMUNITY_MODERATOR", "Tylko moderator społeczności może wykonać tę akcję");

internal sealed class CannotModerateModeratorException()
    : ApiException(StatusCodes.Status403Forbidden, "CANNOT_MODERATE_MODERATOR", "Moderatora może moderować tylko właściciel społeczności");

internal sealed class BannedFromCommunityException()
    : ApiException(StatusCodes.Status403Forbidden, "BANNED_FROM_COMMUNITY", "Masz zakaz uczestnictwa w tej społeczności");

internal sealed class NoBanToAppealException()
    : ApiException(StatusCodes.Status409Conflict, "NO_BAN_TO_APPEAL", "Nie masz aktywnego bana w tej społeczności");

internal sealed class BanAppealAlreadySubmittedException()
    : ApiException(StatusCodes.Status409Conflict, "BAN_APPEAL_ALREADY_SUBMITTED", "Odwołanie od tego bana zostało już złożone");

internal sealed class BanAppealNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "BAN_APPEAL_NOT_FOUND", "Brak oczekującego odwołania");

internal sealed class BanAppealNotEscalatableException()
    : ApiException(StatusCodes.Status409Conflict, "BAN_APPEAL_NOT_ESCALATABLE", "Do administracji można przekazać tylko odwołanie odrzucone przez moderację społeczności");

internal sealed class EscalatedAppealNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "ESCALATED_APPEAL_NOT_FOUND", "Brak odwołania oczekującego na administrację");
