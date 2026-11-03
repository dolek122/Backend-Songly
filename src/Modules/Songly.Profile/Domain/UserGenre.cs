namespace Songly.Profile.Domain;

/// <summary>A favourite genre of a user (User N—M Genre through <c>user_genres</c>).</summary>
internal sealed class UserGenre
{
    // For EF Core.
    private UserGenre()
    {
    }

    public UserGenre(Guid userId, Guid genreId, DateTimeOffset createdAt)
    {
        UserId = userId;
        GenreId = genreId;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    public Guid GenreId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
