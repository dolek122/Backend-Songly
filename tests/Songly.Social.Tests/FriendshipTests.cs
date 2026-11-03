using Songly.Social.Domain;

namespace Songly.Social.Tests;

public sealed class FriendshipTests
{
    /// <summary>
    /// The canonical pair must follow the order Postgres compares <c>uuid</c> values in — byte by
    /// byte in network order — or the schema's CHECK refuses the row.
    /// </summary>
    [Fact]
    public void The_canonical_pair_follows_the_byte_order_of_postgres()
    {
        for (var i = 0; i < 10_000; i++)
        {
            var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

            var (a, b) = Friendship.CanonicalPair(first, second);

            Assert.True(a.ToByteArray(bigEndian: true).AsSpan().SequenceCompareTo(b.ToByteArray(bigEndian: true)) < 0);
        }
    }

    [Fact]
    public void Both_orders_give_the_same_pair()
    {
        var (x, y) = (Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(Friendship.CanonicalPair(x, y), Friendship.CanonicalPair(y, x));
    }

    [Fact]
    public void The_other_end_is_seen_from_either_side()
    {
        var (x, y) = (Guid.NewGuid(), Guid.NewGuid());
        var friendship = Friendship.Between(x, y, DateTimeOffset.UnixEpoch);

        Assert.Equal(y, friendship.OtherThan(x));
        Assert.Equal(x, friendship.OtherThan(y));
    }
}
