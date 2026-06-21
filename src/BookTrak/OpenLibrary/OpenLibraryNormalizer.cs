using BookTrak.OpenLibrary.Models;
using BookTrak.OpenLibrary.Raw;

namespace BookTrak.OpenLibrary;

/// <summary>
/// Pure raw-DTO -&gt; normalized-model mapping. No I/O. This is the layer that absorbs Open
/// Library's inconsistency (string-or-object text fields are already resolved by the JSON
/// converters on the raw DTOs by the time data reaches here; this class handles the
/// remaining shape quirks — key prefixes, nested author refs, picking a single cover id).
/// </summary>
internal static class OpenLibraryNormalizer
{
    public static NormalizedAuthor NormalizeAuthor(RawAuthor raw)
    {
        return new NormalizedAuthor(
            OpenLibraryId: OlKey.ToBareId(raw.Key),
            Name: raw.Name ?? string.Empty,
            PersonalName: raw.PersonalName,
            AlternateNames: raw.AlternateNames ?? [],
            Bio: raw.Bio,
            BirthDate: raw.BirthDate,
            DeathDate: raw.DeathDate,
            PhotoId: FirstPositiveCoverId(raw.Photos),
            Links: raw.Links?.Where(l => !string.IsNullOrWhiteSpace(l.Url)).Select(l => l.Url!).ToList() ?? [],
            Wikipedia: raw.Wikipedia);
    }

    public static NormalizedWork NormalizeWork(RawWork raw)
    {
        return new NormalizedWork(
            OpenLibraryWorkId: OlKey.ToBareId(raw.Key),
            Title: raw.Title ?? string.Empty,
            Subtitle: raw.Subtitle,
            Description: raw.Description,
            FirstPublishDate: raw.FirstPublishDate,
            Subjects: raw.Subjects ?? [],
            AuthorOpenLibraryIds: ExtractAuthorKeys(raw),
            PrimaryCoverId: FirstPositiveCoverId(raw.Covers));
    }

    public static List<string> ExtractAuthorKeys(RawWork raw)
    {
        if (raw.Authors is null)
        {
            return [];
        }

        return raw.Authors
            .Select(a => OlKey.ToBareId(a.Author?.Key))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
    }

    public static NormalizedEdition NormalizeEdition(RawEdition raw)
    {
        var works = raw is RawIsbnEdition isbnEdition
            ? isbnEdition.Works?.Select(w => OlKey.ToBareId(w.Key)).Where(id => id is not null).Select(id => id!).ToList() ?? []
            : new List<string>();

        return new NormalizedEdition(
            OpenLibraryEditionId: OlKey.ToBareId(raw.Key),
            Isbn10: raw.Isbn10?.FirstOrDefault(),
            Isbn13: raw.Isbn13?.FirstOrDefault(),
            NumberOfPages: raw.NumberOfPages,
            Language: OlKey.ToBareId(raw.Languages?.FirstOrDefault()?.Key),
            Publisher: raw.Publishers?.FirstOrDefault(),
            PublishDate: raw.PublishDate,
            PrimaryCoverId: FirstPositiveCoverId(raw.Covers),
            WorkOpenLibraryIds: works);
    }

    public static NormalizedRatings NormalizeRatings(RawRatings raw)
    {
        return new NormalizedRatings(raw.Summary?.Average, raw.Summary?.Count);
    }

    public static NormalizedSearchWork NormalizeSearchWork(RawSearchWorkDoc raw)
    {
        return new NormalizedSearchWork(
            OpenLibraryWorkId: OlKey.ToBareId(raw.Key),
            Title: raw.Title ?? string.Empty,
            AuthorNames: raw.AuthorName ?? [],
            FirstPublishYear: raw.FirstPublishYear,
            PrimaryCoverId: raw.CoverI is > 0 ? raw.CoverI.Value.ToString() : null,
            RatingsAverage: raw.RatingsAverage,
            RatingsCount: raw.RatingsCount);
    }

    public static NormalizedSearchAuthor NormalizeSearchAuthor(RawSearchAuthorDoc raw)
    {
        return new NormalizedSearchAuthor(
            OpenLibraryId: OlKey.ToBareId(raw.Key),
            Name: raw.Name ?? string.Empty,
            BirthDate: raw.BirthDate,
            WorkCount: raw.WorkCount);
    }

    private static string? FirstPositiveCoverId(List<int>? covers)
    {
        // Open Library uses -1 as a "no cover" sentinel in some endpoints.
        var id = covers?.FirstOrDefault(c => c > 0);
        return id is > 0 ? id.Value.ToString() : null;
    }
}
