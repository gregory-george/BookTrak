using System.Text.Json.Serialization;
using BookTrak.OpenLibrary.Json;

namespace BookTrak.OpenLibrary.Raw;

/// <summary>GET /works/{id}.json — also the shape of each entry in /authors/{id}/works.json.</summary>
public sealed class RawWork
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("description")]
    [JsonConverter(typeof(OlTextConverter))]
    public string? Description { get; set; }

    [JsonPropertyName("first_publish_date")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? FirstPublishDate { get; set; }

    [JsonPropertyName("subjects")]
    public List<string>? Subjects { get; set; }

    [JsonPropertyName("covers")]
    public List<int>? Covers { get; set; }

    [JsonPropertyName("authors")]
    public List<RawWorkAuthorRef>? Authors { get; set; }
}

public sealed class RawWorkAuthorRef
{
    [JsonPropertyName("author")]
    public RawKeyRef? Author { get; set; }
}

/// <summary>GET /authors/{id}/works.json</summary>
public sealed class RawAuthorWorksResponse
{
    [JsonPropertyName("entries")]
    public List<RawWork>? Entries { get; set; }

    [JsonPropertyName("size")]
    public int? Size { get; set; }
}
