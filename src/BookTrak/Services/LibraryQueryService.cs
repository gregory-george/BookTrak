using System.Data;
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
}

public sealed record LibraryQuery(string? SearchText = null, LibrarySortOrder Sort = LibrarySortOrder.DateAddedDesc, bool IncludeIgnored = false);

public sealed record LibraryBookSummary(
    int Id,
    string Title,
    string? CoverWebPath,
    string AuthorNames,
    BookStatus Status,
    double? MyRating,
    double? AverageRating,
    DateTime DateAdded,
    string? PublishDate);

public sealed record AuthorSummary(int Id, string Name, string? PhotoWebPath, int BookCount);

public sealed record AuthorDetailResult(Author Author, IReadOnlyList<LibraryBookSummary> Books);

public sealed record BookDetailResult(Book Book, IReadOnlyList<Author> Authors, IReadOnlyList<Edition> Editions);

/// <summary>Read-only query surface backing the browse UI. Every method opens a short-lived
/// context via IDbContextFactory and disposes it before returning — never holds one across an
/// await boundary that spans UI interaction.</summary>
public interface ILibraryQueryService
{
    Task<IReadOnlyList<LibraryBookSummary>> GetLibraryAsync(LibraryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthorSummary>> GetAuthorsAsync(CancellationToken cancellationToken = default);

    Task<AuthorDetailResult?> GetAuthorDetailAsync(int authorId, bool includeIgnored = false, CancellationToken cancellationToken = default);

    Task<BookDetailResult?> GetBookDetailAsync(int bookId, bool includeIgnoredEditions = false, CancellationToken cancellationToken = default);
}

internal sealed class LibraryQueryService(IDbContextFactory<BookTrakContext> contextFactory) : ILibraryQueryService
{
    public async Task<IReadOnlyList<LibraryBookSummary>> GetLibraryAsync(LibraryQuery query, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var booksQuery = context.Books
            .Include(b => b.PreferredEdition)
            .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author)
            .AsQueryable();

        if (!query.IncludeIgnored)
        {
            booksQuery = booksQuery.Where(b => !b.IsIgnored);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var matchingIds = await SearchBookIdsAsync(context, query.SearchText, cancellationToken).ConfigureAwait(false);
            booksQuery = booksQuery.Where(b => matchingIds.Contains(b.Id));
        }

        booksQuery = query.Sort switch
        {
            LibrarySortOrder.TitleAsc => booksQuery.OrderBy(b => b.Title),
            LibrarySortOrder.PublishDateDesc => booksQuery
                .OrderByDescending(b => b.PreferredEdition != null ? b.PreferredEdition.PublishDate : null)
                .ThenByDescending(b => b.DateAdded),
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

    public async Task<IReadOnlyList<AuthorSummary>> GetAuthorsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var authors = await context.Authors
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, a.PhotoPath, BookCount = a.BookAuthors.Count })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return authors.Select(a => new AuthorSummary(a.Id, a.Name, ToWebPath(a.PhotoPath), a.BookCount)).ToList();
    }

    public async Task<AuthorDetailResult?> GetAuthorDetailAsync(int authorId, bool includeIgnored = false, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var author = await context.Authors
            .Include(a => a.BookAuthors).ThenInclude(ba => ba.Book).ThenInclude(b => b.PreferredEdition)
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

    private static LibraryBookSummary ToSummary(Book b) => new(
        b.Id,
        b.Title,
        ToWebPath(b.PreferredEdition?.CoverPath),
        string.Join(", ", b.BookAuthors.Select(ba => ba.Author.Name)),
        b.Status,
        b.MyRating,
        b.AverageRating,
        b.DateAdded,
        b.PreferredEdition?.PublishDate);

    private static string? ToWebPath(string? coverPath) => CoverPaths.ToWebPath(coverPath);

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
