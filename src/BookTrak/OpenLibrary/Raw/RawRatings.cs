using System.Text.Json.Serialization;

namespace BookTrak.OpenLibrary.Raw;

/// <summary>GET /works/{id}/ratings.json</summary>
public sealed class RawRatings
{
    [JsonPropertyName("summary")]
    public RawRatingsSummary? Summary { get; set; }
}

public sealed class RawRatingsSummary
{
    [JsonPropertyName("average")]
    public double? Average { get; set; }

    [JsonPropertyName("count")]
    public int? Count { get; set; }
}
