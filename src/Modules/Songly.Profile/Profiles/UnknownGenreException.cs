using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Profile.Profiles;

internal sealed class UnknownGenreException()
    : ApiException(StatusCodes.Status400BadRequest, "UNKNOWN_GENRE", "Jeden lub więcej gatunków nie istnieje");
