using System.Text.Json.Serialization;
using BookTrak.OpenLibrary.Json;

namespace BookTrak.OpenLibrary.Raw;

/// <summary>GET /search.json</summary>
public sealed class RawSearchWorksResponse
{
    [JsonPropertyName("docs")]
    public List<RawSearchWorkDoc>? Docs { get; set; }
}

public sealed class RawSearchWorkDoc
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author_name")]
    public List<string>? AuthorName { get; set; }

    [JsonPropertyName("first_publish_year")]
    public int? FirstPublishYear { get; set; }

    [JsonPropertyName("cover_i")]
    public int? CoverI { get; set; }

    [JsonPropertyName("ratings_average")]
    public double? RatingsAverage { get; set; }

    [JsonPropertyName("ratings_count")]
    public int? RatingsCount { get; set; }
}

/// <summary>GET /search/authors.json</summary>
public sealed class RawSearchAuthorsResponse
{
    [JsonPropertyName("docs")]
    public List<RawSearchAuthorDoc>? Docs { get; set; }
}

public sealed class RawSearchAuthorDoc
{
    /// <summary>Bare id, e.g. "OL26320A" — this endpoint omits the "/authors/" prefix.</summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("birth_date")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? BirthDate { get; set; }

    [JsonPropertyName("work_count")]
    public int? WorkCount { get; set; }

    [JsonPropertyName("top_work")]
    public string? TopWork { get; set; }
}
