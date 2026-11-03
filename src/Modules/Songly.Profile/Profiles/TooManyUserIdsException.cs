using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Profile.Profiles;

/// <summary>
/// Too many ids in one request for names (original ADR 0036). The lists that ask have a few to a
/// few dozen entries; the limit keeps the endpoint from becoming a dump of the whole profile table
/// in one request.
/// </summary>
internal sealed class TooManyUserIdsException(int limit)
    : ApiException(StatusCodes.Status400BadRequest, "TOO_MANY_USER_IDS", $"Można zapytać o najwyżej {limit} użytkowników naraz");
