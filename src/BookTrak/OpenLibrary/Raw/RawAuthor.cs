using System.Text.Json.Serialization;
using BookTrak.OpenLibrary.Json;

namespace BookTrak.OpenLibrary.Raw;

/// <summary>GET /authors/{id}.json</summary>
public sealed class RawAuthor
{
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("personal_name")]
    public string? PersonalName { get; set; }

    [JsonPropertyName("alternate_names")]
    public List<string>? AlternateNames { get; set; }

    [JsonPropertyName("bio")]
    [JsonConverter(typeof(OlTextConverter))]
    public string? Bio { get; set; }

    [JsonPropertyName("birth_date")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? BirthDate { get; set; }

    [JsonPropertyName("death_date")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? DeathDate { get; set; }

    [JsonPropertyName("photos")]
    public List<int>? Photos { get; set; }

    [JsonPropertyName("links")]
    public List<RawAuthorLink>? Links { get; set; }

    [JsonPropertyName("wikipedia")]
    public string? Wikipedia { get; set; }
}

public sealed class RawAuthorLink
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
