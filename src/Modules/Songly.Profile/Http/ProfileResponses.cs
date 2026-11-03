namespace Songly.Profile.Http;

internal sealed record GenreResponse(Guid Id, string Name);

/// <summary>The caller's own profile.</summary>
internal sealed record ProfileResponse(Guid UserId, string DisplayName, string? Bio, string? AvatarUrl);

/// <summary>
/// Another user's profile, with their favourite genres — public data (original Claude.md §5).
/// Deliberately without what they are listening to: that is behavioural data with a privacy model
/// of its own (original Claude.md §9), not part of this view.
/// </summary>
internal sealed record PublicProfileResponse(Guid UserId, string DisplayName, string? Bio, string? AvatarUrl, IReadOnlyList<GenreResponse> FavoriteGenres);

/// <summary>
/// A user search hit — the minimum needed to send a friend request (<c>addresseeId</c>) and show the
/// person on a list. No bio: the search is open to every logged-in user.
/// </summary>
internal sealed record UserSearchResponse(Guid UserId, string DisplayName, string? AvatarUrl);

/// <summary>
/// Name and avatar for the administration (original ADR 0036). No bio and no genres: the admin
/// lists carry bare ids, and this is exactly what it takes to know who they are about.
/// </summary>
/// <param name="Suspended">
/// Regular users cannot see this profile (original ADR 0026). The administrator sees it anyway, but
/// should know they are looking at something hidden.
/// </param>
internal sealed record ProfileSummaryResponse(Guid UserId, string DisplayName, string? AvatarUrl, bool Suspended);
