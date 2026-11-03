using Microsoft.EntityFrameworkCore;
using Songly.Common.Paging;
using Songly.Identity.Contracts;
using Songly.Profile.Http;
using Songly.Profile.Persistence;

namespace Songly.Profile.Profiles;

/// <summary>
/// Finding people by display name — to add them as friends (the social module sends a friend
/// request by <c>addresseeId</c>, which there was no other way to get).
/// </summary>
internal sealed class UserSearch(ProfileDbContext db, ISuspendedAccounts suspendedAccounts)
{
    /// <summary>
    /// A case-insensitive fragment of the display name. A blank query is an empty page — never
    /// "everyone". The caller is left out of their own results.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Blocked users are not filtered out</b>, deliberately: blocks are the social module's data,
    /// and profile cannot depend on social (social depends on profile — a cycle). A friend request
    /// to someone blocked is refused by the social module anyway.
    /// </para>
    /// <para>
    /// <b>Suspended accounts are</b> (original ADR 0026), and that is not inconsistent: a block is a
    /// relation between two people this module may not know about, a suspension is the state of an
    /// account — identity's data, which profile already depends on. Without it, a platform
    /// sanction left the harasser in the search, exactly where people look for them.
    /// </para>
    /// <para>
    /// The filter is <b>in the query</b>, not applied to the page afterwards: filtering after the
    /// fact breaks paging (a page of 20 comes back with 19, and <c>totalElements</c> counts the
    /// hidden ones). The excluded set always contains the caller, so it is never empty — in SQL
    /// <c>NOT IN ()</c> is true for no row, and an empty set of suspensions, the normal case, would
    /// otherwise return nothing to anyone.
    /// </para>
    /// </remarks>
    public async Task<PagedResponse<UserSearchResponse>> SearchAsync(Guid requesterId, string query, PageQuery page, CancellationToken cancellationToken)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return PagingExtensions.Empty<UserSearchResponse>(page);
        }

        var excluded = new HashSet<Guid>(await suspendedAccounts.SuspendedUserIdsAsync(cancellationToken)) { requesterId };
        var pattern = $"%{EscapeLike(trimmed)}%";
        return await db.Profiles
            .AsNoTracking()
            .Where(profile => EF.Functions.ILike(profile.DisplayName, pattern, LikeEscape) && !excluded.Contains(profile.UserId))
            .OrderBy(profile => profile.DisplayName)
            .ThenBy(profile => profile.UserId)
            .ToPageAsync(page, profile => new UserSearchResponse(profile.UserId, profile.DisplayName, AvatarUrls.Of(profile)), cancellationToken);
    }

    private const string LikeEscape = "\\";

    /// <summary>
    /// <c>%</c> and <c>_</c> in a query mean themselves, not a LIKE pattern. Without this, "%"
    /// matched everyone — the whole user list, page by page, exactly what a blank query
    /// deliberately does not return.
    /// </summary>
    internal static string EscapeLike(string query) =>
        query.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);
}
