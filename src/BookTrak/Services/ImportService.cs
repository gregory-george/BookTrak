using BookTrak.Data.Entities;
using BookTrak.Import;

namespace BookTrak.Services;

public enum ImportRowOutcome
{
    Imported,
    Skipped,
    Unresolved,
}

public sealed record ImportRowResult(int RowNumber, string? Title, ImportRowOutcome Outcome, string Detail);

public sealed record ImportResult(int Imported, int Skipped, int Unresolved, IReadOnlyList<ImportRowResult> Rows);

/// <summary>Imports a Goodreads/StoryGraph CSV export. Every row is resolved to a book via the
/// same add-by-ISBN path used elsewhere (BookTrak.Services.LibraryWriteService.AddByIsbnAsync),
/// which already dedupes against existing editions and throttles/caches Open Library calls — no
/// separate rate-limit layer is needed here. Rows are processed strictly sequentially so a
/// large first import doesn't burst OL/cover requests.</summary>
public interface IImportService
{
    Task<ImportResult> ImportAsync(string csvContent, IProgress<ImportRowResult>? progress = null, CancellationToken cancellationToken = default);
}

internal sealed class ImportService(ILibraryWriteService libraryWriteService) : IImportService
{
    public async Task<ImportResult> ImportAsync(string csvContent, IProgress<ImportRowResult>? progress = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReadingExportRow> rows;
        try
        {
            rows = ReadingExportParser.Parse(csvContent);
        }
        catch (UnrecognizedExportFormatException ex)
        {
            var failure = new ImportRowResult(0, null, ImportRowOutcome.Unresolved, ex.Message);
            return new ImportResult(0, 0, 1, [failure]);
        }

        var results = new List<ImportRowResult>();
        var seenIsbns = new HashSet<string>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ProcessRowAsync(row, seenIsbns, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(result);
        }

        return new ImportResult(
            results.Count(r => r.Outcome == ImportRowOutcome.Imported),
            results.Count(r => r.Outcome == ImportRowOutcome.Skipped),
            results.Count(r => r.Outcome == ImportRowOutcome.Unresolved),
            results);
    }

    private async Task<ImportRowResult> ProcessRowAsync(ReadingExportRow row, HashSet<string> seenIsbns, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.Isbn) && string.IsNullOrWhiteSpace(row.Title))
        {
            return new ImportRowResult(row.RowNumber, row.Title, ImportRowOutcome.Skipped, "Blank row.");
        }

        if (string.IsNullOrWhiteSpace(row.Isbn))
        {
            return new ImportRowResult(row.RowNumber, row.Title, ImportRowOutcome.Unresolved, "No ISBN in this row — add manually.");
        }

        if (!seenIsbns.Add(row.Isbn))
        {
            return new ImportRowResult(row.RowNumber, row.Title, ImportRowOutcome.Skipped, "Duplicate ISBN within this file.");
        }

        AddBookResult? added;
        try
        {
            added = await libraryWriteService.AddByIsbnAsync(row.Isbn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ImportRowResult(row.RowNumber, row.Title, ImportRowOutcome.Unresolved, ex.Message);
        }

        if (added is null)
        {
            return new ImportRowResult(row.RowNumber, row.Title, ImportRowOutcome.Unresolved, $"Open Library has no edition for ISBN '{row.Isbn}'.");
        }

        if (row.Status != BookStatus.None || row.Rating is not null || row.DateRead is not null)
        {
            await libraryWriteService.UpdateStatusAsync(
                added.BookId,
                row.Status,
                dateStarted: null,
                dateRead: row.Status == BookStatus.Read ? row.DateRead : null,
                myRating: row.Status == BookStatus.Read ? row.Rating : null,
                readEditionId: null,
                cancellationToken).ConfigureAwait(false);
        }

        return new ImportRowResult(
            row.RowNumber,
            row.Title,
            ImportRowOutcome.Imported,
            added.WasAlreadyInLibrary ? "Already in library — reading state updated." : "Added.");
    }

}
