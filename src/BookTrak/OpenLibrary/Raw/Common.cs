using System.Text.Json.Serialization;

namespace BookTrak.OpenLibrary.Raw;

/// <summary>Open Library represents references (to an author, a language, etc.) as `{"key": "/authors/OL123A"}`.</summary>
public sealed class RawKeyRef
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }
}
