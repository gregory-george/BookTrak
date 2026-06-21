using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookTrak.OpenLibrary.Json;

/// <summary>
/// Open Library returns free-text fields like `description` and `bio` as either a plain
/// string or an object `{"type": "/type/text", "value": "..."}`. This converter normalizes
/// both shapes down to a plain nullable string so the rest of the app never has to think
/// about it again.
/// </summary>
internal sealed class OlTextConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                {
                    if (doc.RootElement.TryGetProperty("value", out var valueProp) &&
                        valueProp.ValueKind == JsonValueKind.String)
                    {
                        return valueProp.GetString();
                    }

                    return null;
                }
            default:
                reader.Skip();
                return null;
        }
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
