using Songly.Common.Validation;

namespace Songly.Profile.Http;

/// <summary>Replaces the whole set. A set, as in the original: a genre listed twice counts once.</summary>
internal sealed record SetFavoriteGenresRequest
{
    [NotEmpty]
    public HashSet<Guid>? GenreIds { get; init; }
}
