using Songly.Common.Paging;

namespace Songly.Common.Tests;

/// <summary>Spring Data's bounds, which the clients were written against.</summary>
public sealed class PageQueryTests
{
    [Theory]
    [InlineData(0, 20, 0, 20)]
    [InlineData(-3, 20, 0, 20)]
    [InlineData(2, 0, 2, 20)]
    [InlineData(2, -1, 2, 20)]
    [InlineData(2, 100, 2, 100)]
    [InlineData(2, 101, 2, 100)]
    public void Out_of_range_values_are_brought_into_range(int page, int size, int expectedPage, int expectedSize)
    {
        var query = new PageQuery(page, size);

        Assert.Equal(expectedPage, query.Page);
        Assert.Equal(expectedSize, query.Size);
    }

    /// <summary>An int offset would wrap around to a negative number — some other page.</summary>
    [Fact]
    public void The_offset_does_not_overflow() =>
        Assert.Equal((long)int.MaxValue * 100, new PageQuery(int.MaxValue, 100).Offset);
}
