using Songly.Identity.PasswordReset;

namespace Songly.Identity.Tests;

/// <summary>The reset e-mail says how long the link lives — in grammatical Polish.</summary>
public sealed class PolishDurationTests
{
    [Theory]
    [InlineData(1, "1 minutę")]
    [InlineData(2, "2 minuty")]
    [InlineData(4, "4 minuty")]
    [InlineData(5, "5 minut")]
    [InlineData(12, "12 minut")]
    [InlineData(13, "13 minut")]
    [InlineData(14, "14 minut")]
    [InlineData(22, "22 minuty")]
    [InlineData(30, "30 minut")]
    [InlineData(112, "112 minut")]
    [InlineData(122, "122 minuty")]
    public void Minutes(int minutes, string expected) =>
        Assert.Equal(expected, PolishDuration.Format(TimeSpan.FromMinutes(minutes)));

    [Theory]
    [InlineData(1, "1 godzinę")]
    [InlineData(2, "2 godziny")]
    [InlineData(5, "5 godzin")]
    [InlineData(24, "24 godziny")]
    public void Whole_hours_are_said_in_hours(int hours, string expected) =>
        Assert.Equal(expected, PolishDuration.Format(TimeSpan.FromHours(hours)));

    [Fact]
    public void Ninety_minutes_stay_minutes() =>
        Assert.Equal("90 minut", PolishDuration.Format(TimeSpan.FromMinutes(90)));
}
