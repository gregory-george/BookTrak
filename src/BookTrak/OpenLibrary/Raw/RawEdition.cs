using System.Text.Json.Serialization;
using BookTrak.OpenLibrary.Json;

namespace BookTrak.OpenLibrary.Raw;

/// <summary>An entry in /works/{id}/editions.json, and the base shape of /isbn/{isbn}.json.</summary>
public class RawEdition
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("isbn_10")]
    public List<string>? Isbn10 { get; set; }

    [JsonPropertyName("isbn_13")]
    public List<string>? Isbn13 { get; set; }

    [JsonPropertyName("number_of_pages")]
    public int? NumberOfPages { get; set; }

    [JsonPropertyName("languages")]
    public List<RawKeyRef>? Languages { get; set; }

    [JsonPropertyName("publishers")]
    public List<string>? Publishers { get; set; }

    [JsonPropertyName("publish_date")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? PublishDate { get; set; }

    [JsonPropertyName("covers")]
    public List<int>? Covers { get; set; }
}

/// <summary>GET /works/{id}/editions.json</summary>
public sealed class RawEditionsResponse
{
    [JsonPropertyName("entries")]
    public List<RawEdition>? Entries { get; set; }

    [JsonPropertyName("size")]
    public int? Size { get; set; }
}

/// <summary>GET /isbn/{isbn}.json — an edition with a back-reference to its work(s).</summary>
public sealed class RawIsbnEdition : RawEdition
{
    [JsonPropertyName("works")]
    public List<RawKeyRef>? Works { get; set; }
}
