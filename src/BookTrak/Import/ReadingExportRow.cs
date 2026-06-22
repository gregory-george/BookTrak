using BookTrak.Data.Entities;

namespace BookTrak.Import;

/// <summary>A single CSV row, normalized to BookTrak's shape regardless of which export
/// (Goodreads or StoryGraph) it came from. 1-based RowNumber is the original CSV line (including
/// the header) so the import report can point the user back at the source file.</summary>
public sealed record ReadingExportRow(
    int RowNumber,
    string? Title,
    string? Author,
    string? Isbn,
    double? Rating,
    DateTime? DateRead,
    BookStatus Status);
