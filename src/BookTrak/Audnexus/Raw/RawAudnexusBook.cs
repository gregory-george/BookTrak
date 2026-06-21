using System.Text.Json.Serialization;
using BookTrak.OpenLibrary.Json;

namespace BookTrak.Audnexus.Raw;

internal sealed class RawAudnexusBook
{
    public string? Asin { get; set; }

    public string? Title { get; set; }

    public string? Subtitle { get; set; }

    public List<RawAudnexusPerson>? Authors { get; set; }

    public List<RawAudnexusPerson>? Narrators { get; set; }

    public string? PublisherName { get; set; }

    /// <summary>Free text — audnexus sends an ISO-8601 timestamp but treat it as opaque, never
    /// parse as DateTime (same rule as Open Library's publish_date).</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ReleaseDate { get; set; }

    public int? RuntimeLengthMin { get; set; }

    public string? Image { get; set; }

    public string? Summary { get; set; }

    public RawAudnexusSeries? SeriesPrimary { get; set; }

    public string? Language { get; set; }
}

internal sealed class RawAudnexusPerson
{
    public string? Name { get; set; }
}

internal sealed class RawAudnexusSeries
{
    public string? Name { get; set; }

    /// <summary>SeriesPosition is a string ("3", "3.5"), not a number — see CLAUDE.md.</summary>
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Position { get; set; }
}
