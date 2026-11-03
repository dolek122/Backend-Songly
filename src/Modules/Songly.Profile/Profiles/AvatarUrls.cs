using Songly.Profile.Domain;

namespace Songly.Profile.Profiles;

/// <summary>
/// The public address of a profile's avatar.
/// </summary>
/// <remarks>
/// Always <c>null</c> for now: avatars come with media storage (uploads go through the backend and
/// are stripped of metadata, original ADR 0010 and 0047), and until then no profile can get one.
/// Every response builds its <c>avatarUrl</c> here, so that is one place to change.
/// </remarks>
internal static class AvatarUrls
{
    public static string? Of(UserProfile profile) => null;
}
