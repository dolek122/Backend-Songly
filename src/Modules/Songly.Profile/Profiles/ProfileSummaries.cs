using Microsoft.EntityFrameworkCore;
using Songly.Identity.Contracts;
using Songly.Profile.Http;
using Songly.Profile.Persistence;

namespace Songly.Profile.Profiles;

/// <summary>
/// Names for the administration (original ADR 0036), suspended users included.
/// </summary>
/// <remarks>
/// The list of suspensions, the escalation queue and the list of administrators carry bare ids:
/// they live in identity and community, and identity cannot depend on profile (a cycle). Fetching
/// names through <c>GET /profiles/{id}</c> did not work for suspended users, because their profile
/// is hidden (404, original ADR 0026) — that is, exactly for the people the administration decides
/// about. Hiding a suspended account's content protects other users; from the administrator it
/// would only hide whom their own decision concerns.
/// </remarks>
internal sealed class ProfileSummaries(ProfileDbContext db, ISuspendedAccounts suspendedAccounts, AvatarFiles avatars)
{
    public const int MaxIds = 100;

    /// <summary>Unknown ids are simply absent from the result: a deleted account in the moderation history is normal.</summary>
    public async Task<IReadOnlyList<ProfileSummaryResponse>> ForAsync(IReadOnlySet<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count > MaxIds)
        {
            throw new TooManyUserIdsException(MaxIds);
        }

        var suspended = await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken);
        var profiles = await db.Profiles
            .AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .OrderBy(profile => profile.UserId)
            .ToListAsync(cancellationToken);
        return profiles
            .Select(profile => new ProfileSummaryResponse(profile.UserId, profile.DisplayName, avatars.UrlOf(profile), suspended.Contains(profile.UserId)))
            .ToList();
    }
}
