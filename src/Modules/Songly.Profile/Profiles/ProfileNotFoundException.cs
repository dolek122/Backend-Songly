using Microsoft.AspNetCore.Http;
using Songly.Common.Web;

namespace Songly.Profile.Profiles;

internal sealed class ProfileNotFoundException()
    : ApiException(StatusCodes.Status404NotFound, "PROFILE_NOT_FOUND", "Profil nie został znaleziony");
