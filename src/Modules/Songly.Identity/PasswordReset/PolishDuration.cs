namespace Songly.Identity.PasswordReset;

/// <summary>
/// "30 minut", "1 godzinę", "22 minuty" — a duration as a Polish sentence fragment, in the
/// accusative ("wygasa za ...").
/// </summary>
internal static class PolishDuration
{
    public static string Format(TimeSpan duration)
    {
        var minutes = (long)duration.TotalMinutes;
        if (minutes > 0 && minutes % 60 == 0)
        {
            var hours = minutes / 60;
            return $"{hours} {Plural(hours, "godzinę", "godziny", "godzin")}";
        }

        return $"{minutes} {Plural(minutes, "minutę", "minuty", "minut")}";
    }

    /// <summary>
    /// Polish agrees the noun with the number. The 12–14 exception is not pedantry: without it the
    /// result is "13 minuty", because the last digit is the same as in "3 minuty".
    /// </summary>
    internal static string Plural(long count, string one, string few, string many)
    {
        if (count == 1)
        {
            return one;
        }

        var lastTwo = count % 100;
        var last = count % 10;
        return last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? few : many;
    }
}
