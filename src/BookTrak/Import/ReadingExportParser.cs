using System.Globalization;
using BookTrak.Data.Entities;

namespace BookTrak.Import;

public sealed class UnrecognizedExportFormatException : Exception
{
    public UnrecognizedExportFormatException()
        : base("Unrecognized CSV format — expected a Goodreads or StoryGraph export.")
    {
    }
}

/// <summary>Detects Goodreads ("Exclusive Shelf" column) vs StoryGraph ("Read Status" column)
/// export format from the header row and maps each data row to a normalized
/// <see cref="ReadingExportRow"/>. Source: spec'd column mappings — ISBN -> add-by-ISBN
/// resolver, My Rating -> MyRating, Date Read -> DateRead, shelf -> Status.</summary>
internal static class ReadingExportParser
{
    public static IReadOnlyList<ReadingExportRow> Parse(string csvContent)
    {
        var table = CsvParser.Parse(csvContent);
        if (table.Count == 0)
        {
            return [];
        }

        var header = table[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            index.TryAdd(header[i], i);
        }

        var isGoodreads = index.ContainsKey("Exclusive Shelf");
        var isStoryGraph = index.ContainsKey("Read Status");
        if (!isGoodreads && !isStoryGraph)
        {
            throw new UnrecognizedExportFormatException();
        }

        var rows = new List<ReadingExportRow>();
        for (var r = 1; r < table.Count; r++)
        {
            var cells = table[r];
            if (cells.Count <= 1 && (cells.Count == 0 || cells[0].Length == 0))
            {
                continue; // blank line
            }

            string? Get(string column) => index.TryGetValue(column, out var i) && i < cells.Count ? cells[i] : null;

            var row = isGoodreads
                ? new ReadingExportRow(
                    r + 1,
                    NullIfBlank(Get("Title")),
                    NullIfBlank(Get("Author")),
                    CleanGoodreadsIsbn(Get("ISBN13")) ?? CleanGoodreadsIsbn(Get("ISBN")),
                    ParseGoodreadsRating(Get("My Rating")),
                    ParseDate(Get("Date Read")),
                    MapShelf(Get("Exclusive Shelf")))
                : new ReadingExportRow(
                    r + 1,
                    NullIfBlank(Get("Title")),
                    NullIfBlank(Get("Authors")),
                    NullIfBlank(Get("ISBN/UID")),
                    ParseRating(Get("Star Rating")),
                    ParseDate(Get("Last Date Read")),
                    MapShelf(Get("Read Status")));

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Goodreads wraps ISBN cells as ="9780000000000" (an Excel formula trick to keep
    /// leading zeros / prevent numeric reinterpretation) — strip that wrapper.</summary>
    private static string? CleanGoodreadsIsbn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = raw.Trim();
        if (cleaned.StartsWith("=\"", StringComparison.Ordinal) && cleaned.EndsWith('"'))
        {
            cleaned = cleaned[2..^1];
        }

        cleaned = cleaned.Trim('"', '=', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    /// <summary>Goodreads "My Rating" is a whole 0-5 star count where 0 means unrated.</summary>
    private static double? ParseGoodreadsRating(string? raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        return Math.Clamp(value, 0.5, 5.0);
    }

    private static double? ParseRating(string? raw)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return null;
        }

        return Math.Clamp(value, 0.5, 5.0);
    }

    private static readonly string[] DateFormats = ["yyyy/MM/dd", "yyyy-MM-dd", "MM/dd/yyyy"];

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (DateTime.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    /// <summary>Both exports use the same shelf vocabulary: to-read / currently-reading / read.
    /// Anything else (e.g. StoryGraph's "did-not-finish", custom Goodreads shelves) maps to
    /// None — BookTrak's state machine has no equivalent bucket for those.</summary>
    private static BookStatus MapShelf(string? shelf) => shelf?.Trim().ToLowerInvariant() switch
    {
        "currently-reading" => BookStatus.Reading,
        "read" => BookStatus.Read,
        "to-read" => BookStatus.WantToRead,
        _ => BookStatus.None,
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
