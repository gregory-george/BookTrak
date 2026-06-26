using System.Text.Json;
using System.Text.RegularExpressions;
using BookTrak.Audible;
using BookTrak.Audible.Models;
using BookTrak.Audnexus;
using BookTrak.Audnexus.Models;
using BookTrak.Data;
using BookTrak.Data.Entities;
using BookTrak.Hosting;
using BookTrak.OpenLibrary;
using BookTrak.OpenLibrary.Models;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Services;

public sealed record AuthorSearchResult(
    string? OpenLibraryId,
    string Name,
    string? BirthDate,
    int? WorkCount,
    bool AlreadyInLibrary,
    int? LocalAuthorId);

public sealed record WorkSearchResult(
    string? OpenLibraryWorkId,
    string Title,
    string AuthorNames,
    int? FirstPublishYear,
    double? RatingsAverage,
    int? RatingsCount,
    bool AlreadyInLibrary,
    int? LocalBookId);

public sealed record AddAuthorResult(int AuthorId, bool WasAlreadyInLibrary);

public sealed record AddBookResult(int BookId, bool WasAlreadyInLibrary);

public sealed record GapListWork(string OpenLibraryWorkId, string Title, string? FirstPublishDate, string? CoverThumbnailUrl);

public sealed record GapListResult(int AuthorId, string AuthorName, IReadOnlyList<GapListWork> Works);

public sealed record RefreshResult(bool Success, bool WasOpenLibraryLinked, string? Error, int NewEditionsAdded = 0);

public sealed record RefreshAllItem(string Kind, string Name, bool Success, string? Error);

public sealed record RefreshAllResult(int AuthorsRefreshed, int AuthorsFailed, int BooksRefreshed, int BooksFailed, IReadOnlyList<RefreshAllItem> Items);

public sealed record AddEditionResult(bool Success, int? EditionId, string? Error);

/// <summary>Result of an Audible catalog search for a book: the candidate audiobooks, plus an
/// optional user-facing error when Audible's search was unavailable.</summary>
public sealed record AudiobookSearchResult(IReadOnlyList<AudiobookCandidate> Candidates, string? Error);

/// <summary>A free-text audiobook search hit, paired with whether its ASIN is already attached to
/// a local book (so the UI can link to it instead of offering to add a duplicate).</summary>
public sealed record AudiobookSearchHit(AudiobookCandidate Candidate, bool AlreadyInLibrary, int? LocalBookId);

/// <summary>Result of a free-text audiobook search used by the Add Book fallback: the hits, plus an
/// optional user-facing error when Audible's search was unavailable.</summary>
public sealed record AudiobookSearchHitsResult(IReadOnlyList<AudiobookSearchHit> Hits, string? Error);

/// <summary>Re-sync staleness policy. No specific threshold is mandated by the spec — 30 days is
/// a reasonable default for a personal, session-bounded app.</summary>
public static class SyncStatus
{
    public static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(30);

    public static bool IsStale(DateTime? lastSyncedUtc) => lastSyncedUtc is null || DateTime.UtcNow - lastSyncedUtc.Value > StaleThreshold;
}

/// <summary>Write-side counterpart to <see cref="ILibraryQueryService"/> — search Open Library
/// and turn results (or manual input) into local rows. Every method opens a short-lived context
/// via IDbContextFactory. Dedup relies on the unique OpenLibraryId/WorkId/EditionId indexes:
/// re-adding something already linked returns the existing local row instead of duplicating it.</summary>
public interface ILibraryWriteService
{
    Task<IReadOnlyList<AuthorSearchResult>> SearchAuthorsAsync(string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkSearchResult>> SearchWorksAsync(string query, CancellationToken cancellationToken = default);

    Task<AddAuthorResult> AddAuthorFromOpenLibraryAsync(string openLibraryAuthorId, CancellationToken cancellationToken = default);

    Task<AddAuthorResult> AddManualAuthorAsync(string name, CancellationToken cancellationToken = default);

    Task<AddBookResult> AddBookFromOpenLibraryAsync(string openLibraryWorkId, CancellationToken cancellationToken = default);

    Task<AddBookResult> AddManualBookAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>Free-text audiobook search against Audible's catalog (discovery only — see
    /// CLAUDE.md), used as the Add Book fallback when Open Library has no matching work. Flags any
    /// hit whose ASIN is already attached to a local book. Returns an Error string (and empty hits)
    /// when Audible's search is unavailable rather than throwing.</summary>
    Task<AudiobookSearchHitsResult> SearchAudiobooksAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Book from an audnexus audiobook (by ASIN) and attaches it as the
    /// preferred Audiobook edition — the Add Book fallback for works Open Library doesn't carry.
    /// Authors come from audnexus by name (no OL id) and are deduped against existing authors by
    /// case-insensitive name. If the ASIN is already attached to a book, returns that book instead
    /// of duplicating.</summary>
    Task<AddBookResult> AddBookFromAudiobookAsync(string asin, CancellationToken cancellationToken = default);

    /// <summary>Resolves an ISBN via Open Library's /isbn endpoint and creates (or attaches to) a
    /// Book. Returns null if Open Library has no edition for that ISBN — caller should fall back
    /// to manual entry.</summary>
    Task<AddBookResult?> AddByIsbnAsync(string isbn, CancellationToken cancellationToken = default);

    /// <summary>Fetches every Open Library work for the author, paginating through
    /// /authors/{id}/works.json, and diffs against the books already linked to this local author
    /// (by OpenLibraryWorkId) to produce the gap list. Returns an empty list for manually-entered
    /// authors with no OpenLibraryId to look up.</summary>
    Task<GapListResult> GetGapListAsync(int authorId, CancellationToken cancellationToken = default);

    /// <summary>Re-pulls bio/photo/dates for an OL-linked author and stamps LastSyncedUtc. No-op
    /// (returns WasOpenLibraryLinked=false) for manually-entered authors.</summary>
    Task<RefreshResult> RefreshAuthorAsync(int authorId, CancellationToken cancellationToken = default);

    /// <summary>Re-pulls description/rating for an OL-linked book, adds any editions Open Library
    /// now has that aren't already attached, and stamps LastSyncedUtc. Never touches Status,
    /// MyRating, IsIgnored, or the preferred-edition choice. No-op for manually-entered books.</summary>
    Task<RefreshResult> RefreshBookAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes every stale, OL-linked author and book sequentially (so the existing
    /// PoliteRateLimiter naturally throttles the burst). Tolerates per-record failure and skips
    /// records that are already fresh.</summary>
    Task<RefreshAllResult> RefreshAllAsync(IProgress<RefreshAllItem>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Sets the work-level reading state directly — every field is written as given, with
    /// no destructive defaulting, so any status transition (including skipping states or moving
    /// backward) is just a different combination of these values. MyRating is clamped/rounded to
    /// the nearest half-star.</summary>
    Task UpdateStatusAsync(int bookId, BookStatus status, DateTime? dateStarted, DateTime? dateRead, double? myRating, int? readEditionId, CancellationToken cancellationToken = default);

    /// <summary>Toggles Book.IsIgnored (work-level hide).</summary>
    Task SetBookIgnoredAsync(int bookId, bool isIgnored, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a Book and everything that hangs off it — its Editions,
    /// BookAuthor/BookGenre join rows, and FTS search entry all cascade. The Preferred/Read
    /// edition pointers are nulled first to break the Book↔Edition reference cycle (those FKs are
    /// Restrict). Orphaned cover files are reclaimed by the next OrphanCoverCleanup sweep. No-op if
    /// the book doesn't exist. Authors and Series are shared and left intact.</summary>
    Task DeleteBookAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>Toggles Edition.IsIgnored. If the edition being ignored is the book's preferred
    /// edition, repoints to the best remaining non-ignored edition (same scoring as add-time
    /// auto-pick), or null if none remain.</summary>
    Task SetEditionIgnoredAsync(int editionId, bool isIgnored, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a single Edition. If it was the book's preferred edition,
    /// repoints to the best remaining non-ignored edition (or null if none remain); if it was the
    /// recorded read edition, that pointer is nulled. The cover file becomes an orphan the next
    /// sweep clears. No-op if the edition doesn't exist.</summary>
    Task DeleteEditionAsync(int editionId, CancellationToken cancellationToken = default);

    /// <summary>Makes the given edition the book's preferred edition, so its cover becomes the
    /// book's display cover. No-op if the edition is already preferred. Throws if the edition
    /// doesn't exist.</summary>
    Task SetPreferredEditionAsync(int editionId, CancellationToken cancellationToken = default);

    /// <summary>Enriches a new audiobook Edition from audnexus by ASIN and attaches it to the
    /// given Book. If an edition with that ASIN is already attached, returns it unchanged
    /// (Success=true) instead of duplicating. Falls back to a user-facing error — never throws —
    /// so the UI can offer manual entry when audnexus is unavailable or has no match.</summary>
    Task<AddEditionResult> AddAudiobookEditionByAsinAsync(int bookId, string asin, CancellationToken cancellationToken = default);

    /// <summary>Enriches an existing Book with a Physical edition by picking the chosen Open Library
    /// work's best edition (same auto-pick scoring as add-time) and attaching it. Dedups by OL
    /// edition id against editions already on the book. Returns a user-facing error — never throws —
    /// when Open Library is unavailable or the work has no editions, so the UI can fall back to
    /// manual entry.</summary>
    Task<AddEditionResult> AddEditionFromOpenLibraryWorkAsync(int bookId, string openLibraryWorkId, CancellationToken cancellationToken = default);

    /// <summary>Searches Audible's catalog for audiobooks matching a book's title + primary author
    /// (discovery only — see CLAUDE.md), returning candidates for the user to pick from. Returns
    /// an Error string (and empty candidates) when Audible's search is unavailable rather than
    /// throwing, so the UI can fall back to manual ASIN entry.</summary>
    Task<AudiobookSearchResult> SearchAudiobookCandidatesAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>Best-effort: searches Audible for a matching audiobook and, only when there is a
    /// single high-confidence match, attaches it via <see cref="AddAudiobookEditionByAsinAsync"/>.
    /// Skips silently on no/ambiguous match or any failure — never throws. Returns true iff an
    /// edition was attached. Used by the single-add flow.</summary>
    Task<bool> TryAutoAttachAudiobookAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>Manual edition entry fallback for any format (print/ebook/audiobook) — used when
    /// Open Library/audnexus don't have the printing, or audnexus is unavailable.</summary>
    Task<AddEditionResult> AddManualEditionAsync(
        int bookId,
        EditionFormat format,
        string? isbn,
        int? numberOfPages,
        string? language,
        string? publisher,
        string? publishDate,
        string? narrator,
        int? durationMinutes,
        string? audioPublisher,
        string? asin,
        CancellationToken cancellationToken = default);

    /// <summary>Manually sets (or clears, when <paramref name="seriesName"/> is blank) a book's
    /// series and position. Finds an existing manually-named Series by case-insensitive name
    /// match before creating a new one, so re-typing the same series name on multiple books
    /// links them together instead of creating duplicates.</summary>
    Task SetBookSeriesAsync(int bookId, string? seriesName, string? seriesPosition, CancellationToken cancellationToken = default);
}

internal sealed class LibraryWriteService(
    IDbContextFactory<BookTrakContext> contextFactory,
    IOpenLibraryClient openLibraryClient,
    ICoverCacheService coverCache,
    IAudiobookMetadataProvider audiobookMetadataProvider,
    IAudiobookSearchProvider audiobookSearchProvider) : ILibraryWriteService
{
    public async Task<IReadOnlyList<AuthorSearchResult>> SearchAuthorsAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = await openLibraryClient.SearchAuthorsAsync(query, cancellationToken).ConfigureAwait(false);
        if (results.Count == 0)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var olIds = results.Select(r => r.OpenLibraryId).Where(id => id is not null).Select(id => id!).ToList();
        var existing = await context.Authors
            .Where(a => a.OpenLibraryId != null && olIds.Contains(a.OpenLibraryId))
            .Select(a => new { a.Id, a.OpenLibraryId })
            .ToDictionaryAsync(a => a.OpenLibraryId!, a => a.Id, cancellationToken)
            .ConfigureAwait(false);

        return results.Select(r =>
        {
            var localId = r.OpenLibraryId is not null && existing.TryGetValue(r.OpenLibraryId, out var id) ? id : (int?)null;
            return new AuthorSearchResult(r.OpenLibraryId, r.Name, r.BirthDate, r.WorkCount, localId is not null, localId);
        }).ToList();
    }

    public async Task<IReadOnlyList<WorkSearchResult>> SearchWorksAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = await openLibraryClient.SearchWorksAsync(query, cancellationToken).ConfigureAwait(false);
        if (results.Count == 0)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var olIds = results.Select(r => r.OpenLibraryWorkId).Where(id => id is not null).Select(id => id!).ToList();
        var existing = await context.Books
            .Where(b => b.OpenLibraryWorkId != null && olIds.Contains(b.OpenLibraryWorkId))
            .Select(b => new { b.Id, b.OpenLibraryWorkId })
            .ToDictionaryAsync(b => b.OpenLibraryWorkId!, b => b.Id, cancellationToken)
            .ConfigureAwait(false);

        return results.Select(r =>
        {
            var localId = r.OpenLibraryWorkId is not null && existing.TryGetValue(r.OpenLibraryWorkId, out var id) ? id : (int?)null;
            return new WorkSearchResult(
                r.OpenLibraryWorkId,
                r.Title,
                string.Join(", ", r.AuthorNames),
                r.FirstPublishYear,
                r.RatingsAverage,
                r.RatingsCount,
                localId is not null,
                localId);
        }).ToList();
    }

    public async Task<AddAuthorResult> AddAuthorFromOpenLibraryAsync(string openLibraryAuthorId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.Authors.FirstOrDefaultAsync(a => a.OpenLibraryId == openLibraryAuthorId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new AddAuthorResult(existing.Id, true);
        }

        var author = await BuildAuthorFromOpenLibraryAsync(openLibraryAuthorId, cancellationToken).ConfigureAwait(false);
        context.Authors.Add(author);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AddAuthorResult(author.Id, false);
    }

    public async Task<AddAuthorResult> AddManualAuthorAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var author = new Author { Name = name, DateAdded = DateTime.UtcNow };
        context.Authors.Add(author);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AddAuthorResult(author.Id, false);
    }

    public async Task<AddBookResult> AddBookFromOpenLibraryAsync(string openLibraryWorkId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existing = await context.Books.FirstOrDefaultAsync(b => b.OpenLibraryWorkId == openLibraryWorkId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new AddBookResult(existing.Id, true);
        }

        var work = await openLibraryClient.GetWorkAsync(openLibraryWorkId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Open Library work '{openLibraryWorkId}' could not be retrieved.");

        var editions = await openLibraryClient.GetWorkEditionsAsync(openLibraryWorkId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var bestEdition = PickPreferredEdition(editions);

        var book = await BuildBookAsync(context, work, bestEdition, cancellationToken).ConfigureAwait(false);

        return new AddBookResult(book.Id, false);
    }

    public async Task<AddBookResult> AddManualBookAsync(string title, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = new Book { Title = title, DateAdded = DateTime.UtcNow };
        context.Books.Add(book);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AddBookResult(book.Id, false);
    }

    public async Task<AudiobookSearchHitsResult> SearchAudiobooksAsync(string query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AudiobookCandidate> candidates;
        try
        {
            // Audible's catalog search is keyword-based; passing the whole query as the title (with
            // no separate author) is exactly what a free-text "title and/or author" box wants.
            candidates = await audiobookSearchProvider.SearchAsync(query, string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (AudibleUnavailableException ex)
        {
            return new AudiobookSearchHitsResult([], ex.Message);
        }

        if (candidates.Count == 0)
        {
            return new AudiobookSearchHitsResult([], null);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var asins = candidates.Select(c => c.Asin).ToList();
        var byAsin = await context.Editions
            .Where(e => e.Asin != null && asins.Contains(e.Asin))
            .Select(e => new { Asin = e.Asin!, e.BookId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var bookIdByAsin = byAsin
            .GroupBy(e => e.Asin)
            .ToDictionary(g => g.Key, g => g.First().BookId);

        var hits = candidates.Select(c =>
        {
            var localId = bookIdByAsin.TryGetValue(c.Asin, out var id) ? id : (int?)null;
            return new AudiobookSearchHit(c, localId is not null, localId);
        }).ToList();

        return new AudiobookSearchHitsResult(hits, null);
    }

    public async Task<AddBookResult> AddBookFromAudiobookAsync(string asin, CancellationToken cancellationToken = default)
    {
        asin = asin.Trim();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Dedup: if any edition already carries this ASIN, the book exists — return it untouched.
        var existingEdition = await context.Editions.FirstOrDefaultAsync(e => e.Asin == asin, cancellationToken).ConfigureAwait(false);
        if (existingEdition is not null)
        {
            return new AddBookResult(existingEdition.BookId, true);
        }

        NormalizedAudiobook? normalized;
        try
        {
            normalized = await audiobookMetadataProvider.GetByAsinAsync(asin, cancellationToken).ConfigureAwait(false);
        }
        catch (AudnexusUnavailableException ex)
        {
            throw new InvalidOperationException($"audnexus is unavailable — couldn't fetch audiobook '{asin}'.", ex);
        }

        if (normalized is null)
        {
            throw new InvalidOperationException($"audnexus has no audiobook for ASIN '{asin}'.");
        }

        var coverPath = !string.IsNullOrWhiteSpace(normalized.ImageUrl)
            ? ToRelativePath(await coverCache.GetExternalCoverPathAsync(normalized.ImageUrl, $"asin-{asin}", cancellationToken).ConfigureAwait(false))
            : null;

        // No OpenLibraryWorkId — this is a local book sourced from audnexus, not refreshable via OL.
        var book = new Book
        {
            Title = normalized.Title,
            Subtitle = normalized.Subtitle,
            Description = normalized.Summary,
            DateAdded = DateTime.UtcNow,
        };
        context.Books.Add(book);

        foreach (var authorName in normalized.Authors)
        {
            if (string.IsNullOrWhiteSpace(authorName))
            {
                continue;
            }

            var author = await GetOrCreateAuthorByNameAsync(context, authorName, cancellationToken).ConfigureAwait(false);
            book.BookAuthors.Add(new BookAuthor { Book = book, Author = author });
        }

        if (!string.IsNullOrWhiteSpace(normalized.SeriesName))
        {
            book.Series = await GetOrCreateSeriesAsync(context, normalized.SeriesName, cancellationToken).ConfigureAwait(false);
            book.SeriesPosition = normalized.SeriesPosition;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var edition = BuildAudiobookEdition(normalized, book.Id, asin, coverPath);
        context.Editions.Add(edition);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        book.PreferredEditionId = edition.Id;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AddBookResult(book.Id, false);
    }

    public async Task<AddBookResult?> AddByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var normalizedIsbn = NormalizeIsbn(isbn);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existingEdition = await context.Editions
            .FirstOrDefaultAsync(e => e.Isbn10 == normalizedIsbn || e.Isbn13 == normalizedIsbn, cancellationToken)
            .ConfigureAwait(false);
        if (existingEdition is not null)
        {
            return new AddBookResult(existingEdition.BookId, true);
        }

        var edition = await openLibraryClient.GetEditionByIsbnAsync(normalizedIsbn, cancellationToken).ConfigureAwait(false);
        if (edition is null)
        {
            return null;
        }

        var workId = edition.WorkOpenLibraryIds.FirstOrDefault();
        if (workId is null)
        {
            return null;
        }

        var existingBook = await context.Books.FirstOrDefaultAsync(b => b.OpenLibraryWorkId == workId, cancellationToken).ConfigureAwait(false);
        if (existingBook is not null)
        {
            if (edition.OpenLibraryEditionId is not null)
            {
                var alreadyAttached = await context.Editions.AnyAsync(
                    e => e.BookId == existingBook.Id && e.OpenLibraryEditionId == edition.OpenLibraryEditionId,
                    cancellationToken).ConfigureAwait(false);
                if (alreadyAttached)
                {
                    return new AddBookResult(existingBook.Id, true);
                }
            }

            var newEdition = await BuildEditionAsync(edition, existingBook.Id, cancellationToken).ConfigureAwait(false);
            context.Editions.Add(newEdition);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (existingBook.PreferredEditionId is null)
            {
                existingBook.PreferredEditionId = newEdition.Id;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return new AddBookResult(existingBook.Id, false);
        }

        var work = await openLibraryClient.GetWorkAsync(workId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Open Library work '{workId}' could not be retrieved.");

        var book = await BuildBookAsync(context, work, edition, cancellationToken).ConfigureAwait(false);
        return new AddBookResult(book.Id, false);
    }

    public async Task<GapListResult> GetGapListAsync(int authorId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var author = await context.Authors.FirstOrDefaultAsync(a => a.Id == authorId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Author {authorId} not found.");

        if (author.OpenLibraryId is null)
        {
            return new GapListResult(author.Id, author.Name, []);
        }

        var localWorkIds = await context.BookAuthors
            .Where(ba => ba.AuthorId == authorId && ba.Book.OpenLibraryWorkId != null)
            .Select(ba => ba.Book.OpenLibraryWorkId!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var localWorkIdSet = localWorkIds.ToHashSet();

        var allWorks = new List<NormalizedWork>();
        const int pageSize = 50;
        const int maxOffset = 2000; // safety valve against runaway pagination for very prolific authors
        var offset = 0;
        while (true)
        {
            var page = await openLibraryClient.GetAuthorWorksAsync(author.OpenLibraryId, pageSize, offset, cancellationToken).ConfigureAwait(false);
            allWorks.AddRange(page);

            if (page.Count < pageSize || offset >= maxOffset)
            {
                break;
            }

            offset += pageSize;
        }

        var seen = new HashSet<string>();
        var gap = new List<GapListWork>();
        foreach (var work in allWorks)
        {
            if (work.OpenLibraryWorkId is null || !seen.Add(work.OpenLibraryWorkId) || localWorkIdSet.Contains(work.OpenLibraryWorkId))
            {
                continue;
            }

            gap.Add(new GapListWork(
                work.OpenLibraryWorkId,
                work.Title,
                work.FirstPublishDate,
                work.PrimaryCoverId is not null ? $"https://covers.openlibrary.org/b/id/{work.PrimaryCoverId}-S.jpg" : null));
        }

        return new GapListResult(author.Id, author.Name, gap);
    }

    public async Task<RefreshResult> RefreshAuthorAsync(int authorId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var author = await context.Authors.FirstOrDefaultAsync(a => a.Id == authorId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Author {authorId} not found.");

        if (author.OpenLibraryId is null)
        {
            return new RefreshResult(false, false, "Not linked to Open Library.");
        }

        NormalizedAuthor? normalized;
        try
        {
            normalized = await openLibraryClient.GetAuthorAsync(author.OpenLibraryId, cancellationToken).ConfigureAwait(false);
        }
        catch (OpenLibraryUnavailableException ex)
        {
            return new RefreshResult(false, true, ex.Message);
        }

        if (normalized is null)
        {
            return new RefreshResult(false, true, "Open Library no longer has this author.");
        }

        // OL-sourced fields only — Status/MyRating/IsIgnored/PreferredEditionId don't exist on
        // Author, but the same "never clobber local state" rule applies to DateAdded here.
        author.Name = normalized.Name;
        author.PersonalName = normalized.PersonalName;
        author.AlternateNames = normalized.AlternateNames.Count > 0 ? JsonSerializer.Serialize(normalized.AlternateNames) : null;
        author.Bio = normalized.Bio;
        author.BirthDate = normalized.BirthDate;
        author.DeathDate = normalized.DeathDate;
        author.Links = normalized.Links.Count > 0 ? JsonSerializer.Serialize(normalized.Links) : null;
        author.Wikipedia = normalized.Wikipedia;

        if (normalized.PhotoId is not null && normalized.PhotoId != author.PhotoId)
        {
            var photoPath = ToRelativePath(await coverCache.GetAuthorPhotoPathAsync(author.OpenLibraryId, CoverSize.Medium, cancellationToken).ConfigureAwait(false));
            author.PhotoId = normalized.PhotoId;
            author.PhotoPath = photoPath ?? author.PhotoPath;
        }

        author.LastSyncedUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RefreshResult(true, true, null);
    }

    public async Task<RefreshResult> RefreshBookAsync(int bookId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.Include(b => b.Editions).FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        if (book.OpenLibraryWorkId is null)
        {
            return new RefreshResult(false, false, "Not linked to Open Library.");
        }

        NormalizedWork? work;
        NormalizedRatings? ratings;
        IReadOnlyList<NormalizedEdition> editions;
        try
        {
            work = await openLibraryClient.GetWorkAsync(book.OpenLibraryWorkId, cancellationToken).ConfigureAwait(false);
            ratings = await openLibraryClient.GetWorkRatingsAsync(book.OpenLibraryWorkId, cancellationToken).ConfigureAwait(false);
            editions = await openLibraryClient.GetWorkEditionsAsync(book.OpenLibraryWorkId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OpenLibraryUnavailableException ex)
        {
            return new RefreshResult(false, true, ex.Message);
        }

        if (work is null)
        {
            return new RefreshResult(false, true, "Open Library no longer has this work.");
        }

        // OL-sourced fields only — Status, MyRating, IsIgnored, DateStarted, DateRead,
        // ReadEditionId, DateAdded, and PreferredEditionId are local and never touched here.
        book.Title = work.Title;
        book.Subtitle = work.Subtitle;
        book.Description = work.Description;
        book.FirstPublishDate = work.FirstPublishDate;
        book.Subjects = work.Subjects.Count > 0 ? JsonSerializer.Serialize(work.Subjects) : null;
        book.AverageRating = ratings?.Average;
        book.RatingsCount = ratings?.Count;

        var existingEditionIds = book.Editions
            .Where(e => e.OpenLibraryEditionId != null)
            .Select(e => e.OpenLibraryEditionId!)
            .ToHashSet();

        var newEditionsAdded = 0;
        foreach (var edition in editions)
        {
            if (edition.OpenLibraryEditionId is null || existingEditionIds.Contains(edition.OpenLibraryEditionId))
            {
                continue;
            }

            context.Editions.Add(await BuildEditionAsync(edition, book.Id, cancellationToken).ConfigureAwait(false));
            newEditionsAdded++;
        }

        book.LastSyncedUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RefreshResult(true, true, null, newEditionsAdded);
    }

    public async Task<RefreshAllResult> RefreshAllAsync(IProgress<RefreshAllItem>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var authors = await context.Authors
            .Where(a => a.OpenLibraryId != null)
            .Select(a => new { a.Id, a.Name, a.LastSyncedUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var books = await context.Books
            .Where(b => b.OpenLibraryWorkId != null)
            .Select(b => new { b.Id, b.Title, b.LastSyncedUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<RefreshAllItem>();
        int authorsRefreshed = 0, authorsFailed = 0, booksRefreshed = 0, booksFailed = 0;

        // Sequential, not parallel — the shared PoliteRateLimiter inside the OL client already
        // throttles individual calls, but running these one at a time keeps a "refresh all" burst
        // polite without adding a second layer of concurrency control here.
        foreach (var a in authors)
        {
            if (!SyncStatus.IsStale(a.LastSyncedUtc))
            {
                continue;
            }

            RefreshResult result;
            try
            {
                result = await RefreshAuthorAsync(a.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new RefreshResult(false, true, ex.Message);
            }

            if (result.Success) authorsRefreshed++; else authorsFailed++;
            var item = new RefreshAllItem("Author", a.Name, result.Success, result.Error);
            items.Add(item);
            progress?.Report(item);
        }

        foreach (var b in books)
        {
            if (!SyncStatus.IsStale(b.LastSyncedUtc))
            {
                continue;
            }

            RefreshResult result;
            try
            {
                result = await RefreshBookAsync(b.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = new RefreshResult(false, true, ex.Message);
            }

            if (result.Success) booksRefreshed++; else booksFailed++;
            var item = new RefreshAllItem("Book", b.Title, result.Success, result.Error);
            items.Add(item);
            progress?.Report(item);
        }

        return new RefreshAllResult(authorsRefreshed, authorsFailed, booksRefreshed, booksFailed, items);
    }

    public async Task UpdateStatusAsync(int bookId, BookStatus status, DateTime? dateStarted, DateTime? dateRead, double? myRating, int? readEditionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        book.Status = status;
        book.DateStarted = dateStarted;
        book.DateRead = dateRead;
        book.MyRating = myRating is { } rating ? ClampToHalfStar(rating) : null;
        book.ReadEditionId = readEditionId;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetBookIgnoredAsync(int bookId, bool isIgnored, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        book.IsIgnored = isIgnored;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteBookAsync(int bookId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return;
        }

        // Break the Book<->Edition reference cycle first: PreferredEditionId/ReadEditionId are
        // Restrict FKs, so the cascade delete of Editions throws unless they're nulled out. Once
        // the Book is removed, its Editions, BookAuthors, and BookGenres cascade (and the FTS
        // AfterDelete trigger drops the search row). Cover files become orphans the next sweep clears.
        book.PreferredEditionId = null;
        book.ReadEditionId = null;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Books.Remove(book);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEditionIgnoredAsync(int editionId, bool isIgnored, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var edition = await context.Editions
            .Include(e => e.Book).ThenInclude(b => b.Editions)
            .FirstOrDefaultAsync(e => e.Id == editionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Edition {editionId} not found.");

        edition.IsIgnored = isIgnored;

        if (isIgnored && edition.Book.PreferredEditionId == editionId)
        {
            var replacement = PickPreferredLocalEdition(edition.Book.Editions.Where(e => e.Id != editionId && !e.IsIgnored));
            edition.Book.PreferredEditionId = replacement?.Id;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var edition = await context.Editions
            .Include(e => e.Book).ThenInclude(b => b.Editions)
            .FirstOrDefaultAsync(e => e.Id == editionId, cancellationToken).ConfigureAwait(false);
        if (edition is null)
        {
            return;
        }

        var book = edition.Book;

        // Break the Book->Edition Restrict FKs before removing the row, otherwise the delete throws.
        if (book.PreferredEditionId == editionId)
        {
            var replacement = PickPreferredLocalEdition(book.Editions.Where(e => e.Id != editionId && !e.IsIgnored));
            book.PreferredEditionId = replacement?.Id;
        }

        if (book.ReadEditionId == editionId)
        {
            book.ReadEditionId = null;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Editions.Remove(edition);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPreferredEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var edition = await context.Editions
            .Include(e => e.Book)
            .FirstOrDefaultAsync(e => e.Id == editionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Edition {editionId} not found.");

        if (edition.Book.PreferredEditionId == editionId)
        {
            return;
        }

        edition.Book.PreferredEditionId = editionId;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AddEditionResult> AddAudiobookEditionByAsinAsync(int bookId, string asin, CancellationToken cancellationToken = default)
    {
        asin = asin.Trim();

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        var existing = await context.Editions.FirstOrDefaultAsync(e => e.BookId == bookId && e.Asin == asin, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new AddEditionResult(true, existing.Id, null);
        }

        NormalizedAudiobook? normalized;
        try
        {
            normalized = await audiobookMetadataProvider.GetByAsinAsync(asin, cancellationToken).ConfigureAwait(false);
        }
        catch (AudnexusUnavailableException ex)
        {
            return new AddEditionResult(false, null, ex.Message);
        }

        if (normalized is null)
        {
            return new AddEditionResult(false, null, $"audnexus has no audiobook for ASIN '{asin}'.");
        }

        var coverPath = !string.IsNullOrWhiteSpace(normalized.ImageUrl)
            ? ToRelativePath(await coverCache.GetExternalCoverPathAsync(normalized.ImageUrl, $"asin-{asin}", cancellationToken).ConfigureAwait(false))
            : null;

        var edition = BuildAudiobookEdition(normalized, bookId, asin, coverPath);
        context.Editions.Add(edition);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (book.PreferredEditionId is null)
        {
            book.PreferredEditionId = edition.Id;
        }

        // Don't clobber a series the user already set (manually, or from an earlier edition) —
        // only fill it in when the book has none yet.
        if (book.SeriesId is null && !string.IsNullOrWhiteSpace(normalized.SeriesName))
        {
            book.Series = await GetOrCreateSeriesAsync(context, normalized.SeriesName, cancellationToken).ConfigureAwait(false);
            book.SeriesPosition = normalized.SeriesPosition;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AddEditionResult(true, edition.Id, null);
    }

    public async Task<AddEditionResult> AddEditionFromOpenLibraryWorkAsync(int bookId, string openLibraryWorkId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        IReadOnlyList<NormalizedEdition> editions;
        try
        {
            editions = await openLibraryClient.GetWorkEditionsAsync(openLibraryWorkId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OpenLibraryUnavailableException ex)
        {
            return new AddEditionResult(false, null, ex.Message);
        }

        var best = PickPreferredEdition(editions);
        if (best is null)
        {
            return new AddEditionResult(false, null, "Open Library has no editions for that book — add one manually instead.");
        }

        // Dedup against editions already attached to this book (re-picking the same printing is a no-op).
        if (best.OpenLibraryEditionId is not null)
        {
            var existing = await context.Editions
                .FirstOrDefaultAsync(e => e.BookId == bookId && e.OpenLibraryEditionId == best.OpenLibraryEditionId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return new AddEditionResult(true, existing.Id, null);
            }
        }

        var entity = await BuildEditionAsync(best, bookId, cancellationToken).ConfigureAwait(false);
        context.Editions.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (book.PreferredEditionId is null)
        {
            book.PreferredEditionId = entity.Id;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AddEditionResult(true, entity.Id, null);
    }

    public async Task<AudiobookSearchResult> SearchAudiobookCandidatesAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var (title, author) = await GetSearchTermsAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (title is null)
        {
            throw new InvalidOperationException($"Book {bookId} not found.");
        }

        try
        {
            var candidates = await audiobookSearchProvider.SearchAsync(title, author ?? string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new AudiobookSearchResult(candidates, null);
        }
        catch (AudibleUnavailableException ex)
        {
            return new AudiobookSearchResult([], ex.Message);
        }
    }

    public async Task<bool> TryAutoAttachAudiobookAsync(int bookId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (title, author, authors) = await GetMatchTermsAsync(bookId, cancellationToken).ConfigureAwait(false);
            if (title is null)
            {
                return false;
            }

            var candidates = await audiobookSearchProvider.SearchAsync(title, author ?? string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
            var match = AudiobookMatch.PickConfident(candidates, title, authors);
            if (match is null)
            {
                return false;
            }

            var result = await AddAudiobookEditionByAsinAsync(bookId, match.Asin, cancellationToken).ConfigureAwait(false);
            return result.Success;
        }
        catch (Exception)
        {
            // Best-effort: a failed audiobook lookup must never block adding the book.
            return false;
        }
    }

    private async Task<(string? Title, string? Author)> GetSearchTermsAsync(int bookId, CancellationToken cancellationToken)
    {
        var (title, author, _) = await GetMatchTermsAsync(bookId, cancellationToken).ConfigureAwait(false);
        return (title, author);
    }

    /// <summary>Loads a book's title plus its author names (in BookAuthor order) for Audible
    /// search/matching. Returns (null, null, []) when the book doesn't exist.</summary>
    private async Task<(string? Title, string? Author, IReadOnlyList<string> Authors)> GetMatchTermsAsync(int bookId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books
            .Include(b => b.BookAuthors).ThenInclude(ba => ba.Author)
            .FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false);
        if (book is null)
        {
            return (null, null, []);
        }

        var authors = book.BookAuthors.Select(ba => ba.Author.Name).ToList();
        return (book.Title, authors.FirstOrDefault(), authors);
    }

    public async Task SetBookSeriesAsync(int bookId, string? seriesName, string? seriesPosition, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        if (string.IsNullOrWhiteSpace(seriesName))
        {
            book.SeriesId = null;
            book.SeriesPosition = null;
        }
        else
        {
            book.Series = await GetOrCreateSeriesAsync(context, seriesName, cancellationToken).ConfigureAwait(false);
            book.SeriesPosition = string.IsNullOrWhiteSpace(seriesPosition) ? null : seriesPosition.Trim();
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Builds an Audiobook Edition from an audnexus result. Shared by the
    /// attach-to-existing-book and create-book-from-audiobook flows.</summary>
    private static Edition BuildAudiobookEdition(NormalizedAudiobook normalized, int bookId, string asin, string? coverPath) => new()
    {
        BookId = bookId,
        Format = EditionFormat.Audiobook,
        Asin = asin,
        Language = normalized.Language,
        Publisher = normalized.PublisherName,
        PublishDate = normalized.ReleaseDate,
        Narrator = normalized.Narrators.Count > 0 ? string.Join(", ", normalized.Narrators) : null,
        DurationSeconds = normalized.RuntimeLengthMinutes is { } minutes ? minutes * 60 : null,
        AudioPublisher = normalized.PublisherName,
        CoverPath = coverPath,
    };

    /// <summary>Dedups audnexus authors (which have no OL id) against existing rows by
    /// case-insensitive name, so a name BookTrak already knows — whether added from OL or another
    /// audiobook — links instead of creating a duplicate. New names become manual Author rows.</summary>
    private static async Task<Author> GetOrCreateAuthorByNameAsync(BookTrakContext context, string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var existing = await context.Authors.FirstOrDefaultAsync(a => a.Name.ToLower() == trimmed.ToLower(), cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var author = new Author { Name = trimmed, DateAdded = DateTime.UtcNow };
        context.Authors.Add(author);
        return author;
    }

    /// <summary>Open Library's own series data is too patchy to key off (per spec) — series are
    /// purely local, identified by case-insensitive name match so audnexus and manual entry
    /// naturally converge on the same row when they agree on a name.</summary>
    private static async Task<Series> GetOrCreateSeriesAsync(BookTrakContext context, string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var existing = await context.Series.FirstOrDefaultAsync(s => s.Name.ToLower() == trimmed.ToLower(), cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var series = new Series { Name = trimmed };
        context.Series.Add(series);
        return series;
    }

    public async Task<AddEditionResult> AddManualEditionAsync(
        int bookId,
        EditionFormat format,
        string? isbn,
        int? numberOfPages,
        string? language,
        string? publisher,
        string? publishDate,
        string? narrator,
        int? durationMinutes,
        string? audioPublisher,
        string? asin,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var book = await context.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        var normalizedIsbn = !string.IsNullOrWhiteSpace(isbn) ? NormalizeIsbn(isbn) : null;

        var edition = new Edition
        {
            BookId = bookId,
            Format = format,
            Isbn10 = normalizedIsbn is { Length: 10 } ? normalizedIsbn : null,
            Isbn13 = normalizedIsbn is { Length: 13 } ? normalizedIsbn : null,
            Asin = !string.IsNullOrWhiteSpace(asin) ? asin.Trim() : null,
            NumberOfPages = numberOfPages,
            Language = language,
            Publisher = publisher,
            PublishDate = publishDate,
            Narrator = format == EditionFormat.Audiobook ? narrator : null,
            DurationSeconds = format == EditionFormat.Audiobook && durationMinutes is { } minutes ? minutes * 60 : null,
            AudioPublisher = format == EditionFormat.Audiobook ? audioPublisher : null,
        };
        context.Editions.Add(edition);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (book.PreferredEditionId is null)
        {
            book.PreferredEditionId = edition.Id;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AddEditionResult(true, edition.Id, null);
    }

    private static double ClampToHalfStar(double rating) => Math.Round(Math.Clamp(rating, 0.5, 5.0) * 2, MidpointRounding.AwayFromZero) / 2;

    /// <summary>Same scoring as the add-time auto-pick (English -> ISBN -> cover -> page count ->
    /// newest publish date -> first), applied to local Edition rows for ignore-repoint.</summary>
    private static Edition? PickPreferredLocalEdition(IEnumerable<Edition> candidates)
    {
        var list = candidates.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        return list
            .Select((edition, index) => (Edition: edition, Index: index))
            .OrderByDescending(x => LanguageScore(x.Edition.Language))
            .ThenByDescending(x => x.Edition.Isbn13 is not null || x.Edition.Isbn10 is not null)
            .ThenByDescending(x => x.Edition.CoverPath is not null)
            .ThenByDescending(x => x.Edition.NumberOfPages is not null)
            .ThenByDescending(x => ExtractYear(x.Edition.PublishDate) ?? -1)
            .ThenBy(x => x.Index)
            .First().Edition;
    }

    /// <summary>Creates the Book, its authors (fetched/deduped from OL), and the supplied edition
    /// (set as preferred), then saves. Shared by the OL-search and add-by-ISBN flows.</summary>
    private async Task<Book> BuildBookAsync(BookTrakContext context, NormalizedWork work, NormalizedEdition? edition, CancellationToken cancellationToken)
    {
        var coverId = edition?.PrimaryCoverId ?? work.PrimaryCoverId;
        var coverPath = coverId is not null
            ? ToRelativePath(await coverCache.GetBookCoverPathAsync(coverId, CoverSize.Medium, cancellationToken).ConfigureAwait(false))
            : null;

        var ratings = work.OpenLibraryWorkId is not null
            ? await openLibraryClient.GetWorkRatingsAsync(work.OpenLibraryWorkId, cancellationToken).ConfigureAwait(false)
            : null;

        var book = new Book
        {
            OpenLibraryWorkId = work.OpenLibraryWorkId,
            Title = work.Title,
            Subtitle = work.Subtitle,
            Description = work.Description,
            FirstPublishDate = work.FirstPublishDate,
            Subjects = work.Subjects.Count > 0 ? JsonSerializer.Serialize(work.Subjects) : null,
            AverageRating = ratings?.Average,
            RatingsCount = ratings?.Count,
            DateAdded = DateTime.UtcNow,
            LastSyncedUtc = DateTime.UtcNow,
        };
        context.Books.Add(book);

        foreach (var authorOlId in work.AuthorOpenLibraryIds)
        {
            var author = await GetOrCreateAuthorAsync(context, authorOlId, cancellationToken).ConfigureAwait(false);
            book.BookAuthors.Add(new BookAuthor { Book = book, Author = author });
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (edition is not null)
        {
            var entity = new Edition
            {
                BookId = book.Id,
                OpenLibraryEditionId = edition.OpenLibraryEditionId,
                Format = EditionFormat.Physical,
                Isbn10 = edition.Isbn10,
                Isbn13 = edition.Isbn13,
                NumberOfPages = edition.NumberOfPages,
                Language = edition.Language,
                Publisher = edition.Publisher,
                PublishDate = edition.PublishDate,
                CoverId = coverId,
                CoverPath = coverPath,
            };
            context.Editions.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            book.PreferredEditionId = entity.Id;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return book;
    }

    private async Task<Edition> BuildEditionAsync(NormalizedEdition edition, int bookId, CancellationToken cancellationToken)
    {
        var coverPath = edition.PrimaryCoverId is not null
            ? ToRelativePath(await coverCache.GetBookCoverPathAsync(edition.PrimaryCoverId, CoverSize.Medium, cancellationToken).ConfigureAwait(false))
            : null;

        return new Edition
        {
            BookId = bookId,
            OpenLibraryEditionId = edition.OpenLibraryEditionId,
            Format = EditionFormat.Physical,
            Isbn10 = edition.Isbn10,
            Isbn13 = edition.Isbn13,
            NumberOfPages = edition.NumberOfPages,
            Language = edition.Language,
            Publisher = edition.Publisher,
            PublishDate = edition.PublishDate,
            CoverId = edition.PrimaryCoverId,
            CoverPath = coverPath,
        };
    }

    private async Task<Author> GetOrCreateAuthorAsync(BookTrakContext context, string openLibraryAuthorId, CancellationToken cancellationToken)
    {
        var existing = await context.Authors.FirstOrDefaultAsync(a => a.OpenLibraryId == openLibraryAuthorId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var author = await BuildAuthorFromOpenLibraryAsync(openLibraryAuthorId, cancellationToken).ConfigureAwait(false);
        context.Authors.Add(author);
        return author;
    }

    private async Task<Author> BuildAuthorFromOpenLibraryAsync(string openLibraryAuthorId, CancellationToken cancellationToken)
    {
        var normalized = await openLibraryClient.GetAuthorAsync(openLibraryAuthorId, cancellationToken).ConfigureAwait(false);

        var photoPath = normalized?.PhotoId is not null
            ? ToRelativePath(await coverCache.GetAuthorPhotoPathAsync(openLibraryAuthorId, CoverSize.Medium, cancellationToken).ConfigureAwait(false))
            : null;

        return new Author
        {
            OpenLibraryId = openLibraryAuthorId,
            Name = normalized?.Name ?? openLibraryAuthorId,
            PersonalName = normalized?.PersonalName,
            AlternateNames = normalized is { AlternateNames.Count: > 0 } ? JsonSerializer.Serialize(normalized.AlternateNames) : null,
            Bio = normalized?.Bio,
            BirthDate = normalized?.BirthDate,
            DeathDate = normalized?.DeathDate,
            PhotoId = normalized?.PhotoId,
            PhotoPath = photoPath,
            Links = normalized is { Links.Count: > 0 } ? JsonSerializer.Serialize(normalized.Links) : null,
            Wikipedia = normalized?.Wikipedia,
            DateAdded = DateTime.UtcNow,
            LastSyncedUtc = DateTime.UtcNow,
        };
    }

    /// <summary>Preferred-edition auto-pick: English language -> has ISBN -> has cover -> has
    /// page count, then newest publish date, then the first edition Open Library returned.</summary>
    private static NormalizedEdition? PickPreferredEdition(IReadOnlyList<NormalizedEdition> editions)
    {
        if (editions.Count == 0)
        {
            return null;
        }

        return editions
            .Select((edition, index) => (Edition: edition, Index: index))
            .OrderByDescending(x => LanguageScore(x.Edition.Language))
            .ThenByDescending(x => x.Edition.Isbn13 is not null || x.Edition.Isbn10 is not null)
            .ThenByDescending(x => x.Edition.PrimaryCoverId is not null)
            .ThenByDescending(x => x.Edition.NumberOfPages is not null)
            .ThenByDescending(x => ExtractYear(x.Edition.PublishDate) ?? -1)
            .ThenBy(x => x.Index)
            .First().Edition;
    }

    /// <summary>2 = confirmed English, 1 = language unknown, 0 = confirmed non-English.</summary>
    private static int LanguageScore(string? language) => language switch
    {
        null => 1,
        var l when l.Equals("eng", StringComparison.OrdinalIgnoreCase) || l.Equals("en", StringComparison.OrdinalIgnoreCase) => 2,
        _ => 0,
    };

    private static int? ExtractYear(string? publishDate)
    {
        if (string.IsNullOrWhiteSpace(publishDate))
        {
            return null;
        }

        var match = Regex.Match(publishDate, @"\d{4}");
        return match.Success ? int.Parse(match.Value) : null;
    }

    private static string NormalizeIsbn(string isbn) => isbn.Replace("-", "").Replace(" ", "").Trim();

    /// <summary>Covers are cached to an absolute path on disk; the DB stores the path relative to
    /// the app root so it stays portable (matches CoverPaths.ToWebPath's expectations).</summary>
    private static string? ToRelativePath(string? absolutePath)
        => absolutePath is null ? null : Path.GetRelativePath(AppPaths.RootDirectory, absolutePath);
}
