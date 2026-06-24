using System.Data;
using System.Text.RegularExpressions;
using BookTrak.Data;
using BookTrak.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Services;

public enum LibrarySortOrder
{
    DateAddedDesc,
    TitleAsc,
    AuthorAsc,
    PublishDateDesc,
    RatingDesc,
}

public enum AuthorSortOrder
{
    FirstNameAsc,
    LastNameAsc,
}

/// <summary>Cumulative "X stars & up" buckets (selecting both 4+ and 3+ is redundant but
/// harmless, since multi-select within a facet ORs together) plus an explicit bucket for books
/// with no MyRating.</summary>
public enum RatingBucket
{
    FourPlus,
    ThreePlus,
    TwoPlus,
    OnePlus,
    Unrated,
}

/// <summary>Which facet to skip when building a filtered query — used so a facet's own option
/// counts reflect every OTHER active filter (the standard "narrow" faceted-search semantics)
/// rather than always reflecting the full current selection.</summary>
internal enum FacetKind
{
    Status,
    Format,
    Genre,
    Series,
    Rating,
}

public sealed record LibraryQuery(
    string? SearchText = null,
    LibrarySortOrder Sort = LibrarySortOrder.DateAddedDesc,
    bool IncludeIgnored = false,
    IReadOnlySet<BookStatus>? Statuses = null,
    IReadOnlySet<EditionFormat>? Formats = null,
    IReadOnlySet<int>? GenreIds = null,
    IReadOnlySet<int>? SeriesIds = null,
    IReadOnlySet<RatingBucket>? RatingBuckets = null);

public sealed record LibraryBookSummary(
    int Id,
    string Title,
    string? CoverWebPath,
    string AuthorNames,
    BookStatus Status,
    double? MyRating,
    double? AverageRating,
    DateTime DateAdded,
    string? PublishDate,
    int? EarliestPublishYear,
    int EditionCount);

public sealed record AuthorQuery(string? SearchText = null, AuthorSortOrder Sort = AuthorSortOrder.FirstNameAsc);

public sealed record AuthorSummary(int Id, string Name, string? PhotoWebPath, int BookCount);

public sealed record AuthorDetailResult(Author Author, IReadOnlyList<LibraryBookSummary> Books);

public sealed record BookDetailResult(Book Book, IReadOnlyList<Author> Authors, IReadOnlyList<Edition> Editions);

/// <summary>A single selectable edition for the Reading Log's "read edition" dropdown — just an
/// id and a human label (e.g. "Audiobook — Macmillan Audio").</summary>
public sealed record ReadingLogEdition(int Id, string Label);

/// <summary>One editable row of the Reading Log grid. Unlike <see cref="LibraryBookSummary"/> this
/// carries the reading-progress fields (started/finished dates, read edition) plus the per-book
/// edition list needed to populate the read-edition picker inline.</summary>
public sealed record ReadingLogRow(
    int Id,
    string Title,
    string AuthorNames,
    BookStatus Status,
    DateTime? DateStarted,
    DateTime? DateRead,
    double? MyRating,
    int? ReadEditionId,
    IReadOnlyList<ReadingLogEdition> Editions);

public sealed record SeriesVolume(
    int BookId,
    string Title,
    string? SeriesPosition,
    BookStatus Status,
    string? CoverWebPath);

public sealed record SeriesDetailResult(
    int SeriesId,
    string SeriesName,
    IReadOnlyList<SeriesVolume> Volumes,
    SeriesVolume? NextUp,
    IReadOnlyList<string> MissingPositions);

public sealed record StatusFacetOption(BookStatus Status, int Count);

public sealed record FormatFacetOption(EditionFormat Format, int Count);

public sealed record GenreFacetOption(int GenreId, string Name, int Count);

public sealed record SeriesFacetOption(int SeriesId, string Name, int Count);

public sealed record RatingFacetOption(RatingBucket Bucket, int Count);

public sealed record LibraryFacets(
    IReadOnlyList<StatusFacetOption> Statuses,
    IReadOnlyList<FormatFacetOption> Formats,
    IReadOnlyList<GenreFacetOption> Genres,
    IReadOnlyList<SeriesFacetOption> Series,
    IReadOnlyList<RatingFacetOption> RatingBuckets);

/// <summary>Read-only query surface backing the browse UI. Every method opens a short-lived
/// context via IDbContextFactory and disposes it before returning — never holds one across an
/// await boundary that spans UI interaction.</summary>
public interface ILibraryQueryService
{
    Task<IReadOnlyList<LibraryBookSummary>> GetLibraryAsync(LibraryQuery query, CancellationToken cancellationToken = default);

    /// <summary>Live per-facet option counts for the current query: each facet's own counts are
    /// computed with every OTHER active facet (and the search text / ignored toggle) applied, so
    /// picking an option never makes its own facet list "disappear."</summary>
    Task<LibraryFacets> GetLibraryFacetsAsync(LibraryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthorSummary>> GetAuthorsAsync(AuthorQuery query, CancellationToken cancellationToken = default);

    Task<AuthorDetailResult?> GetAuthorDetailAsync(int authorId, bool includeIgnored = false, CancellationToken cancellationToken = default);

    Task<BookDetailResult?> GetBookDetailAsync(int bookId, bool includeIgnoredEditions = false, CancellationToken cancellationToken = default);

    /// <summary>All non-ignored books with their reading-progress fields and a lightweight edition
    /// list, ordered by title — backs the inline-editable Reading Log grid.</summary>
    Task<IReadOnlyList<ReadingLogRow>> GetReadingLogAsync(CancellationToken cancellationToken = default);

    /// <summary>Series view: volumes ordered by SeriesPosition (numeric-aware — "3.5" sorts
    /// between "3" and "4"; non-numeric/missing positions sort last by title), the "next up"
    /// volume (first unread volume past the highest-numbered Read volume, or the first volume at
    /// all if none are read yet), and any whole-number gaps in the position sequence.</summary>
    Task<SeriesDetailResult?> GetSeriesDetailAsync(int seriesId, CancellationToken cancellationToken = default);
}

internal sealed class LibraryQueryService(IDbContextFactory<BookTrakContext> contextFactory) : ILibraryQueryService
{
    public async Task<IReadOnlyList<LibraryBookSummary>> GetLibraryAsync(LibraryQuery query, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var matchingIds = !string.IsNullOrWhiteSpace(query.SearchText)
            ? await SearchBookIdsAsync(context, query.SearchText, cancellationToken).ConfigureAwait(false)
            : null;

        var booksQuery = ApplyFilters(
            context.Books
                .Include(b => b.PreferredEdition)
                .Include(b => b.Editions)
                .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author),
            query,
            matchingIds,
            exclude: null);

        booksQuery = query.Sort switch
        {
            LibrarySortOrder.TitleAsc => booksQuery.OrderBy(b => b.Title),
            LibrarySortOrder.PublishDateDesc => booksQuery
                .OrderByDescending(b => b.PreferredEdition != null ? b.PreferredEdition.PublishDate : null)
                .ThenByDescending(b => b.DateAdded),
            LibrarySortOrder.RatingDesc => booksQuery
                .OrderByDescending(b => b.MyRating)
                .ThenByDescending(b => b.AverageRating)
                .ThenBy(b => b.Title),
            LibrarySortOrder.AuthorAsc => booksQuery, // author name isn't a column — sort after materializing below
            _ => booksQuery.OrderByDescending(b => b.DateAdded),
        };

        var books = await booksQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<Book> ordered = books;
        if (query.Sort == LibrarySortOrder.AuthorAsc)
        {
            ordered = books.OrderBy(b => b.BookAuthors.Select(ba => ba.Author.Name).FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        return ordered.Select(ToSummary).ToList();
    }

    public async Task<LibraryFacets> GetLibraryFacetsAsync(LibraryQuery query, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var matchingIds = !string.IsNullOrWhiteSpace(query.SearchText)
            ? await SearchBookIdsAsync(context, query.SearchText, cancellationToken).ConfigureAwait(false)
            : null;

        var statusRows = await ApplyFilters(context.Books, query, matchingIds, FacetKind.Status)
            .GroupBy(b => b.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var statuses = statusRows.Select(r => new StatusFacetOption(r.Status, r.Count)).OrderBy(o => o.Status).ToList();

        // SQLite's EF provider can't translate SelectMany over a navigation collection (it would
        // require CROSS APPLY) — materialize the filtered book ids first, then join the child
        // table (Editions/BookGenres) against that id list with a plain WHERE IN.
        var formatBookIds = await ApplyFilters(context.Books, query, matchingIds, FacetKind.Format)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var formatPairs = await context.Editions
            .Where(e => formatBookIds.Contains(e.BookId))
            .Select(e => new { e.BookId, e.Format })
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var formats = formatPairs.GroupBy(x => x.Format)
            .Select(g => new FormatFacetOption(g.Key, g.Count()))
            .OrderBy(o => o.Format).ToList();

        var genreBookIds = await ApplyFilters(context.Books, query, matchingIds, FacetKind.Genre)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var genrePairs = await context.BookGenres
            .Where(bg => genreBookIds.Contains(bg.BookId))
            .Select(bg => new { bg.BookId, bg.GenreId, bg.Genre.Name })
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var genres = genrePairs.GroupBy(x => new { x.GenreId, x.Name })
            .Select(g => new GenreFacetOption(g.Key.GenreId, g.Key.Name, g.Count()))
            .OrderByDescending(o => o.Count).ThenBy(o => o.Name).ToList();

        var seriesRows = await ApplyFilters(context.Books, query, matchingIds, FacetKind.Series)
            .Where(b => b.SeriesId != null)
            .Select(b => new { SeriesId = b.SeriesId!.Value, b.Series!.Name })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var series = seriesRows.GroupBy(x => new { x.SeriesId, x.Name })
            .Select(g => new SeriesFacetOption(g.Key.SeriesId, g.Key.Name, g.Count()))
            .OrderByDescending(o => o.Count).ThenBy(o => o.Name).ToList();

        var ratings = await ApplyFilters(context.Books, query, matchingIds, FacetKind.Rating)
            .Select(b => b.MyRating)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ratingBuckets = new List<RatingFacetOption>
        {
            new(RatingBucket.FourPlus, ratings.Count(r => r >= 4)),
            new(RatingBucket.ThreePlus, ratings.Count(r => r >= 3)),
            new(RatingBucket.TwoPlus, ratings.Count(r => r >= 2)),
            new(RatingBucket.OnePlus, ratings.Count(r => r >= 1)),
            new(RatingBucket.Unrated, ratings.Count(r => r is null)),
        }.Where(o => o.Count > 0).ToList();

        return new LibraryFacets(statuses, formats, genres, series, ratingBuckets);
    }

    public async Task<IReadOnlyList<AuthorSummary>> GetAuthorsAsync(AuthorQuery query, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var authorsQuery = context.Authors.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var search = query.SearchText.Trim();
            authorsQuery = authorsQuery.Where(a => EF.Functions.Like(a.Name, $"%{search}%"));
        }

        var authors = await authorsQuery
            .Select(a => new { a.Id, a.Name, a.PhotoPath, BookCount = a.BookAuthors.Count })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ordered = query.Sort switch
        {
            AuthorSortOrder.LastNameAsc => authors.OrderBy(a => LastName(a.Name), StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
            _ => authors.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.Select(a => new AuthorSummary(a.Id, a.Name, ToWebPath(a.PhotoPath), a.BookCount)).ToList();
    }

    /// <summary>Author.Name is a single free-text field (e.g. "Brandon Sanderson") with no
    /// separate first/last columns, so "sort by last name" takes the last whitespace-separated
    /// token as a best-effort approximation.</summary>
    private static string LastName(string name)
    {
        var trimmed = name.Trim();
        var lastSpace = trimmed.LastIndexOf(' ');
        return lastSpace < 0 ? trimmed : trimmed[(lastSpace + 1)..];
    }

    public async Task<AuthorDetailResult?> GetAuthorDetailAsync(int authorId, bool includeIgnored = false, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var author = await context.Authors
            .Include(a => a.BookAuthors).ThenInclude(ba => ba.Book).ThenInclude(b => b.PreferredEdition)
            .Include(a => a.BookAuthors).ThenInclude(ba => ba.Book).ThenInclude(b => b.Editions)
            .Include(a => a.BookAuthors).ThenInclude(ba => ba.Book).ThenInclude(b => b.BookAuthors).ThenInclude(ba => ba.Author)
            .FirstOrDefaultAsync(a => a.Id == authorId, cancellationToken)
            .ConfigureAwait(false);

        if (author is null)
        {
            return null;
        }

        var books = author.BookAuthors
            .Select(ba => ba.Book)
            .Where(b => includeIgnored || !b.IsIgnored)
            .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToSummary)
            .ToList();

        return new AuthorDetailResult(author, books);
    }

    public async Task<BookDetailResult?> GetBookDetailAsync(int bookId, bool includeIgnoredEditions = false, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books
            .Include(b => b.Series)
            .Include(b => b.PreferredEdition)
            .Include(b => b.Editions)
            .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author)
            .FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken)
            .ConfigureAwait(false);

        if (book is null)
        {
            return null;
        }

        var authors = book.BookAuthors.Select(ba => ba.Author).ToList();
        var editions = book.Editions.Where(e => includeIgnoredEditions || !e.IsIgnored).OrderBy(e => e.Format).ToList();

        return new BookDetailResult(book, authors, editions);
    }

    public async Task<IReadOnlyList<ReadingLogRow>> GetReadingLogAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var books = await context.Books
            .Where(b => !b.IsIgnored)
            .Include(b => b.Editions)
            .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return books
            .OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToReadingLogRow)
            .ToList();
    }

    private static ReadingLogRow ToReadingLogRow(Book b) => new(
        b.Id,
        b.Title,
        string.Join(", ", b.BookAuthors.Select(ba => ba.Author.Name)),
        b.Status,
        b.DateStarted,
        b.DateRead,
        b.MyRating,
        b.ReadEditionId,
        b.Editions
            .Where(e => !e.IsIgnored)
            .OrderBy(e => e.Format)
            .Select(e => new ReadingLogEdition(e.Id, EditionLabel(e)))
            .ToList());

    private static string EditionLabel(Edition e)
        => e.Publisher is { Length: > 0 } publisher ? $"{e.Format} — {publisher}" : e.Format.ToString();

    public async Task<SeriesDetailResult?> GetSeriesDetailAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var series = await context.Series
            .Include(s => s.Books).ThenInclude(b => b.PreferredEdition)
            .FirstOrDefaultAsync(s => s.Id == seriesId, cancellationToken)
            .ConfigureAwait(false);

        if (series is null)
        {
            return null;
        }

        var ordered = series.Books
            .Where(b => !b.IsIgnored)
            .Select(b => (Book: b, Position: ParsePosition(b.SeriesPosition)))
            .OrderBy(x => x.Position ?? double.MaxValue)
            .ThenBy(x => x.Book.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var volumes = ordered.Select(x => new SeriesVolume(
            x.Book.Id,
            x.Book.Title,
            x.Book.SeriesPosition,
            x.Book.Status,
            ToWebPath(x.Book.PreferredEdition?.CoverPath))).ToList();

        var lastReadPosition = ordered
            .Where(x => x.Book.Status == BookStatus.Read && x.Position is not null)
            .Select(x => x.Position!.Value)
            .DefaultIfEmpty(double.NegativeInfinity)
            .Max();
        var anyRead = ordered.Any(x => x.Book.Status == BookStatus.Read);

        var nextUp = anyRead
            ? ordered.FirstOrDefault(x => x.Book.Status != BookStatus.Read && x.Position > lastReadPosition).Book
            : ordered.FirstOrDefault().Book;
        var nextUpVolume = nextUp is null ? null : volumes.First(v => v.BookId == nextUp.Id);

        // Gaps are only meaningful for whole-number main-sequence volumes — side novellas at
        // "3.5" etc. don't count as a "volume 4" that could be missing.
        var wholeNumberPositions = ordered
            .Where(x => x.Position is { } p && Math.Abs(p - Math.Round(p)) < 0.0001)
            .Select(x => (int)Math.Round(x.Position!.Value))
            .ToHashSet();

        var missing = new List<string>();
        if (wholeNumberPositions.Count > 0)
        {
            for (var i = wholeNumberPositions.Min(); i <= wholeNumberPositions.Max(); i++)
            {
                if (!wholeNumberPositions.Contains(i))
                {
                    missing.Add(i.ToString());
                }
            }
        }

        return new SeriesDetailResult(series.Id, series.Name, volumes, nextUpVolume, missing);
    }

    private static double? ParsePosition(string? position)
        => !string.IsNullOrWhiteSpace(position) && double.TryParse(position, out var value) ? value : null;

    /// <summary>Applies the ignored toggle, search-text match, and every active facet except
    /// <paramref name="exclude"/> (pass null for the result-list query, where every facet applies).</summary>
    private static IQueryable<Book> ApplyFilters(IQueryable<Book> source, LibraryQuery query, HashSet<int>? matchingIds, FacetKind? exclude)
    {
        var q = source;

        if (!query.IncludeIgnored)
        {
            q = q.Where(b => !b.IsIgnored);
        }

        if (matchingIds is not null)
        {
            q = q.Where(b => matchingIds.Contains(b.Id));
        }

        if (exclude != FacetKind.Status && query.Statuses is { Count: > 0 } statuses)
        {
            q = q.Where(b => statuses.Contains(b.Status));
        }

        if (exclude != FacetKind.Format && query.Formats is { Count: > 0 } formats)
        {
            q = q.Where(b => b.Editions.Any(e => formats.Contains(e.Format)));
        }

        if (exclude != FacetKind.Genre && query.GenreIds is { Count: > 0 } genreIds)
        {
            q = q.Where(b => b.BookGenres.Any(bg => genreIds.Contains(bg.GenreId)));
        }

        if (exclude != FacetKind.Series && query.SeriesIds is { Count: > 0 } seriesIds)
        {
            q = q.Where(b => b.SeriesId != null && seriesIds.Contains(b.SeriesId.Value));
        }

        if (exclude != FacetKind.Rating && query.RatingBuckets is { Count: > 0 } buckets)
        {
            q = q.Where(b =>
                (buckets.Contains(RatingBucket.FourPlus) && b.MyRating >= 4) ||
                (buckets.Contains(RatingBucket.ThreePlus) && b.MyRating >= 3) ||
                (buckets.Contains(RatingBucket.TwoPlus) && b.MyRating >= 2) ||
                (buckets.Contains(RatingBucket.OnePlus) && b.MyRating >= 1) ||
                (buckets.Contains(RatingBucket.Unrated) && b.MyRating == null));
        }

        return q;
    }

    private static LibraryBookSummary ToSummary(Book b)
    {
        var visibleEditions = b.Editions.Where(e => !e.IsIgnored).ToList();
        var years = visibleEditions.Select(e => ExtractYear(e.PublishDate)).Where(y => y is not null).Select(y => y!.Value).ToList();
        var earliestYear = years.Count > 0 ? years.Min() : (int?)null;

        return new(
            b.Id,
            b.Title,
            ToWebPath(b.PreferredEdition?.CoverPath),
            string.Join(", ", b.BookAuthors.Select(ba => ba.Author.Name)),
            b.Status,
            b.MyRating,
            b.AverageRating,
            b.DateAdded,
            b.PreferredEdition?.PublishDate,
            earliestYear,
            visibleEditions.Count);
    }

    private static string? ToWebPath(string? coverPath) => CoverPaths.ToWebPath(coverPath);

    /// <summary>PublishDate is free text (e.g. "March 1954") — never parsed as DateTime; this
    /// pulls the first 4-digit year for display/sort purposes only.</summary>
    private static int? ExtractYear(string? publishDate)
    {
        if (string.IsNullOrWhiteSpace(publishDate))
        {
            return null;
        }

        var match = Regex.Match(publishDate, @"\d{4}");
        return match.Success ? int.Parse(match.Value) : null;
    }

    /// <summary>Matches the FTS5 BookSearch table (kept in sync via triggers — see the
    /// AddFts5Search migration) and returns the set of matching Book ids.</summary>
    private static async Task<HashSet<int>> SearchBookIdsAsync(BookTrakContext context, string searchText, CancellationToken cancellationToken)
    {
        var ftsQuery = ToFtsQuery(searchText);
        if (ftsQuery is null)
        {
            return [];
        }

        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rowid FROM BookSearch WHERE BookSearch MATCH $q";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$q";
        parameter.Value = ftsQuery;
        command.Parameters.Add(parameter);

        var ids = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    /// <summary>Builds a safe FTS5 MATCH expression: each whitespace-separated token becomes a
    /// quoted prefix match (so partial typing in a live search box returns hits), ANDed
    /// together. Quoting also neutralizes FTS5's query-syntax special characters.</summary>
    private static string? ToFtsQuery(string searchText)
    {
        var terms = searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return null;
        }

        return string.Join(' ', terms.Select(t => $"\"{t.Replace("\"", "\"\"")}\"*"));
    }
}
