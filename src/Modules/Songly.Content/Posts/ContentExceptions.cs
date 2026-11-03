using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Content.Posts;

/// <summary>Also for a post the caller may not see (a block, a suspended author): 403 would confirm it exists.</summary>
internal sealed class PostNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "POST_NOT_FOUND", "Post nie został znaleziony");

internal sealed class CommentNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "COMMENT_NOT_FOUND", "Komentarz nie został znaleziony");

internal sealed class NotResourceOwnerException()
    : ApiException(StatusCodes.Status403Forbidden, "NOT_RESOURCE_OWNER", "Tylko autor może usunąć ten zasób");

/// <summary>Content's own copy of the community module's error: exceptions are internal to their module.</summary>
internal sealed class UnknownCommunityException()
    : ApiException(StatusCodes.Status404NotFound, "COMMUNITY_NOT_FOUND", "Społeczność nie została znaleziona");

internal sealed class NotCommunityMemberException()
    : ApiException(StatusCodes.Status403Forbidden, "NOT_COMMUNITY_MEMBER", "Tylko członek społeczności może w niej publikować");

internal sealed class BannedFromCommunityException()
    : ApiException(StatusCodes.Status403Forbidden, "BANNED_FROM_COMMUNITY", "Masz zakaz uczestnictwa w tej społeczności");

internal sealed class UnknownPostImageException()
    : ApiException(StatusCodes.Status400BadRequest, "UNKNOWN_POST_IMAGE", "Zdjęcie wygasło albo zostało już użyte. Wgraj je ponownie.");
