namespace Songly.Social.Domain;

/// <summary>
/// One friendship is one row, whichever side it is looked at from: the pair is stored in a
/// canonical order, <c>user_a_id &lt; user_b_id</c>, which the schema enforces with a CHECK.
/// </summary>
internal sealed class Friendship
{
    // For EF Core.
    private Friendship()
    {
    }

    private Friendship(Guid userAId, Guid userBId, DateTimeOffset createdAt)
    {
        UserAId = userAId;
        UserBId = userBId;
        CreatedAt = createdAt;
    }

    public Guid UserAId { get; private set; }

    public Guid UserBId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Friendship Between(Guid first, Guid second, DateTimeOffset createdAt)
    {
        var (a, b) = CanonicalPair(first, second);
        return new Friendship(a, b, createdAt);
    }

    /// <summary>
    /// The pair in the order Postgres compares <c>uuid</c> values: byte by byte, which for the
    /// canonical text form is the same as comparing the lowercase hex strings. Compared as strings on
    /// purpose, like the original: it is the order the CHECK constraint enforces, stated in a way
    /// that does not depend on how <see cref="Guid.CompareTo(Guid)"/> happens to be implemented.
    /// </summary>
    public static (Guid A, Guid B) CanonicalPair(Guid first, Guid second) =>
        string.CompareOrdinal(first.ToString(), second.ToString()) < 0 ? (first, second) : (second, first);

    /// <summary>The other end of the friendship, seen from <paramref name="userId"/>.</summary>
    public Guid OtherThan(Guid userId) => UserAId == userId ? UserBId : UserAId;
}
