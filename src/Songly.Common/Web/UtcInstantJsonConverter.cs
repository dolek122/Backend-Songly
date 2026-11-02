using System.Text.Json;
using System.Text.Json.Serialization;

namespace Songly.Common.Web;

/// <summary>
/// Writes every <see cref="DateTimeOffset"/> as UTC with a <c>Z</c> suffix
/// (<c>2026-11-02T12:00:00.123456Z</c>) — the shape a Java <c>Instant</c> has in the original API.
/// The default converter would write the offset (<c>+00:00</c>), or a local one for values that
/// are not UTC, which is a different contract for the mobile client.
/// </summary>
internal sealed class UtcInstantJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.UtcDateTime);
}
