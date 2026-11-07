using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.ModerationLog.History;

/// <summary>
/// Checked before the permission — a 403 on an id that does not exist would suggest something is there. The
/// message is this module's own in the original, not the community module's.
/// </summary>
internal sealed class CommunityLogNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "COMMUNITY_NOT_FOUND", "Nie ma takiej społeczności");

/// <summary>403, not 404: communities are public (original Claude.md §8), so their existence is no secret.</summary>
internal sealed class NotCommunityModeratorException()
    : ApiException(StatusCodes.Status403Forbidden, "NOT_COMMUNITY_MODERATOR", "Tylko moderator społeczności może wykonać tę akcję");
