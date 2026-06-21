using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookTrak.OpenLibrary.Json;

/// <summary>
/// Free-text fields (birth_date, death_date, first_publish_date, publish_date) are almost
/// always JSON strings in Open Library, but occasionally arrive as a bare number (e.g. a
/// publish year). Never parse these as DateTime — keep them as opaque strings. This converter
/// just tolerates the occasional numeric shape so deserialization doesn't throw.
/// </summary>
internal sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            _ => Skip(ref reader),
        };
    }

    private static string? Skip(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
