using System.Text.Json.Serialization;

namespace BookTrak.Audible.Raw;

/// <summary>GET /1.0/catalog/products?keywords=... — Audible's unofficial catalog search.
/// Deserialized with JsonSerializerDefaults.Web; snake_case fields need explicit
/// [JsonPropertyName] since the Web policy only bridges camelCase, not snake_case.</summary>
internal sealed class RawAudibleSearchResponse
{
    public List<RawAudibleProduct>? Products { get; set; }
}

internal sealed class RawAudibleProduct
{
    public string? Asin { get; set; }

    public string? Title { get; set; }

    public string? Subtitle { get; set; }

    public List<RawAudiblePerson>? Authors { get; set; }

    public List<RawAudiblePerson>? Narrators { get; set; }

    [JsonPropertyName("publisher_name")]
    public string? PublisherName { get; set; }

    public string? Language { get; set; }
}

internal sealed class RawAudiblePerson
{
    public string? Name { get; set; }

    public string? Asin { get; set; }
}
