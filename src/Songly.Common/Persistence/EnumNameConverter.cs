using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Songly.Common.Persistence;

/// <summary>
/// Stores an enum as its <c>UPPER_SNAKE_CASE</c> name (<c>Pending</c> → <c>PENDING</c>) — what the
/// original backend's <c>@Enumerated(EnumType.STRING)</c> wrote for its Java enums, so the values
/// already in the schema read back unchanged. The same naming the API uses in JSON.
/// </summary>
public sealed class EnumNameConverter<TEnum>() : ValueConverter<TEnum, string>(
    value => Names[value],
    name => Values[name])
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> Names =
        Enum.GetValues<TEnum>().ToDictionary(value => value, value => JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString()));

    private static readonly Dictionary<string, TEnum> Values = Names.ToDictionary(pair => pair.Value, pair => pair.Key);
}
