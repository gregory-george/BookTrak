using BookTrak.Data;
using BookTrak.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Services;

public sealed record RatingBucketCount(double Rating, int Count);

public sealed record MonthlyCount(int Year, int Month, int AddedCount, int ReadCount);

public sealed record FormatCount(EditionFormat Format, int Count);

public sealed record AuthorRatingStat(int AuthorId, string Name, double AverageRating, int RatedBookCount);

public sealed record GenreRatingStat(int GenreId, string Name, double AverageRating, int RatedBookCount);

public sealed record MostReadAuthor(int AuthorId, string Name, int ReadCount);

public sealed record MostReadNarrator(string Name, int ReadCount);

public sealed record BookExtreme(int BookId, string Title, int Value);

public sealed record ReadingStats(
    int TotalBooksRead,
    int BooksReadThisYear,
    IReadOnlyList<RatingBucketCount> RatingDistribution,
    int TotalPagesRead,
    double TotalHoursListened,
    IReadOnlyList<MonthlyCount> MonthlyTrend,
    IReadOnlyList<FormatCount> FormatBreakdown,
    IReadOnlyList<AuthorRatingStat> TopRatedAuthors,
    IReadOnlyList<GenreRatingStat> TopRatedGenres,
    MostReadAuthor? MostReadAuthor,
    MostReadNarrator? MostReadNarrator,
    BookExtreme? LongestByPages,
    BookExtreme? ShortestByPages,
    BookExtreme? LongestByDurationMinutes,
    BookExtreme? ShortestByDurationMinutes);

/// <summary>Every number here is derived from existing Book/Edition/Author/Genre rows — no new
/// schema (per spec). Per-book page count, duration, narrator, and format come from the edition
/// actually read (Book.ReadEditionId), falling back to the preferred edition when ReadEditionId
/// wasn't specified at "mark as read" time.</summary>
public interface IStatsQueryService
{
    Task<ReadingStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

internal sealed class StatsQueryService(IDbContextFactory<BookTrakContext> contextFactory) : IStatsQueryService
{
    public async Task<ReadingStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;

        // Scalar (non-collection) navigations only — Book.ReadEdition/PreferredEdition are
        // one-to-one references, so this translates to a plain LEFT JOIN, not the
        // SQLite-unsupported CROSS APPLY that a collection SelectMany would require.
        var readBooks = await context.Books
            .Where(b => b.Status == BookStatus.Read)
            .Select(b => new
            {
                b.Id,
                b.Title,
                b.MyRating,
                b.DateRead,
                Format = b.ReadEdition != null ? b.ReadEdition.Format : b.PreferredEdition != null ? b.PreferredEdition.Format : (EditionFormat?)null,
                Pages = b.ReadEdition != null ? b.ReadEdition.NumberOfPages : b.PreferredEdition != null ? b.PreferredEdition.NumberOfPages : null,
                DurationSeconds = b.ReadEdition != null ? b.ReadEdition.DurationSeconds : b.PreferredEdition != null ? b.PreferredEdition.DurationSeconds : null,
                Narrator = b.ReadEdition != null ? b.ReadEdition.Narrator : b.PreferredEdition != null ? b.PreferredEdition.Narrator : null,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var allBookDates = await context.Books
            .Select(b => new { b.DateAdded, b.DateRead })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var totalBooksRead = readBooks.Count;
        var booksReadThisYear = readBooks.Count(b => b.DateRead?.Year == now.Year);

        var ratingCounts = readBooks
            .Where(b => b.MyRating != null)
            .GroupBy(b => b.MyRating!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        var ratingDistribution = Enumerable.Range(1, 10)
            .Select(i => i * 0.5)
            .Select(r => new RatingBucketCount(r, ratingCounts.GetValueOrDefault(r)))
            .OrderByDescending(x => x.Rating)
            .ToList();

        var totalPagesRead = readBooks.Where(b => b.Pages is not null).Sum(b => b.Pages!.Value);
        var totalHoursListened = readBooks.Where(b => b.DurationSeconds is not null).Sum(b => b.DurationSeconds!.Value) / 3600.0;

        var monthsWindow = Enumerable.Range(0, 12)
            .Select(offset => new DateTime(now.Year, now.Month, 1).AddMonths(-11 + offset))
            .ToList();
        var monthlyTrend = monthsWindow
            .Select(m => new MonthlyCount(
                m.Year,
                m.Month,
                allBookDates.Count(b => b.DateAdded.Year == m.Year && b.DateAdded.Month == m.Month),
                allBookDates.Count(b => b.DateRead?.Year == m.Year && b.DateRead?.Month == m.Month)))
            .ToList();

        var formatBreakdown = readBooks
            .Where(b => b.Format is not null)
            .GroupBy(b => b.Format!.Value)
            .Select(g => new FormatCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var readBookIds = readBooks.Select(b => b.Id).ToList();
        var ratedBookIds = readBooks.Where(b => b.MyRating is not null).Select(b => b.Id).ToHashSet();
        var ratingById = readBooks.Where(b => b.MyRating is not null).ToDictionary(b => b.Id, b => b.MyRating!.Value);

        var authorPairs = await context.BookAuthors
            .Where(ba => readBookIds.Contains(ba.BookId))
            .Select(ba => new { ba.BookId, ba.AuthorId, ba.Author.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var mostReadAuthor = authorPairs
            .GroupBy(x => new { x.AuthorId, x.Name })
            .Select(g => new MostReadAuthor(g.Key.AuthorId, g.Key.Name, g.Select(x => x.BookId).Distinct().Count()))
            .OrderByDescending(x => x.ReadCount)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var topRatedAuthors = authorPairs
            .Where(x => ratedBookIds.Contains(x.BookId))
            .GroupBy(x => new { x.AuthorId, x.Name })
            .Select(g => new AuthorRatingStat(g.Key.AuthorId, g.Key.Name, g.Average(x => ratingById[x.BookId]), g.Select(x => x.BookId).Distinct().Count()))
            .OrderByDescending(x => x.AverageRating)
            .ThenByDescending(x => x.RatedBookCount)
            .Take(10)
            .ToList();

        var genrePairs = await context.BookGenres
            .Where(bg => readBookIds.Contains(bg.BookId))
            .Select(bg => new { bg.BookId, bg.GenreId, bg.Genre.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var topRatedGenres = genrePairs
            .Where(x => ratedBookIds.Contains(x.BookId))
            .GroupBy(x => new { x.GenreId, x.Name })
            .Select(g => new GenreRatingStat(g.Key.GenreId, g.Key.Name, g.Average(x => ratingById[x.BookId]), g.Select(x => x.BookId).Distinct().Count()))
            .OrderByDescending(x => x.AverageRating)
            .ThenByDescending(x => x.RatedBookCount)
            .Take(10)
            .ToList();

        var mostReadNarrator = readBooks
            .Where(b => !string.IsNullOrWhiteSpace(b.Narrator))
            .SelectMany(b => b.Narrator!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => (Name: n, b.Id)))
            .GroupBy(x => x.Name)
            .Select(g => new MostReadNarrator(g.Key, g.Select(x => x.Id).Distinct().Count()))
            .OrderByDescending(x => x.ReadCount)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var withPages = readBooks.Where(b => b.Pages is not null).ToList();
        var longestByPages = withPages.OrderByDescending(b => b.Pages).Select(b => new BookExtreme(b.Id, b.Title, b.Pages!.Value)).FirstOrDefault();
        var shortestByPages = withPages.OrderBy(b => b.Pages).Select(b => new BookExtreme(b.Id, b.Title, b.Pages!.Value)).FirstOrDefault();

        var withDuration = readBooks.Where(b => b.DurationSeconds is not null).ToList();
        var longestByDuration = withDuration.OrderByDescending(b => b.DurationSeconds).Select(b => new BookExtreme(b.Id, b.Title, b.DurationSeconds!.Value / 60)).FirstOrDefault();
        var shortestByDuration = withDuration.OrderBy(b => b.DurationSeconds).Select(b => new BookExtreme(b.Id, b.Title, b.DurationSeconds!.Value / 60)).FirstOrDefault();

        return new ReadingStats(
            totalBooksRead,
            booksReadThisYear,
            ratingDistribution,
            totalPagesRead,
            totalHoursListened,
            monthlyTrend,
            formatBreakdown,
            topRatedAuthors,
            topRatedGenres,
            mostReadAuthor,
            mostReadNarrator,
            longestByPages,
            shortestByPages,
            longestByDuration,
            shortestByDuration);
    }
}
