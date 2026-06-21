namespace BookTrak.Data.Entities;

/// <summary>BookTrak's Book is an Open Library Work. Edition-level fields (ISBN, pages,
/// language, cover, publisher, publish date) live on <see cref="Edition"/>, not here.
/// There is no scalar AuthorId — authors are modeled only via <see cref="BookAuthor"/>.</summary>
public class Book
{
    public int Id { get; set; }

    /// <summary>Open Library work key, e.g. "OL123W". Null for manually-entered books.</summary>
    public string? OpenLibraryWorkId { get; set; }

    public required string Title { get; set; }

    public string? Subtitle { get; set; }

    /// <summary>Normalized plain text — OL returns string or {type, value}; normalize on ingest.</summary>
    public string? Description { get; set; }

    /// <summary>Free text, e.g. "1954" — never parse as DateTime.</summary>
    public string? FirstPublishDate { get; set; }

    /// <summary>JSON array of raw, uncontrolled Open Library subjects (not the curated Genre taxonomy).</summary>
    public string? Subjects { get; set; }

    public int? SeriesId { get; set; }

    public Series? Series { get; set; }

    /// <summary>String, not numeric — allows "3", "3.5", "0.5".</summary>
    public string? SeriesPosition { get; set; }

    /// <summary>From Open Library's /works/{id}/ratings.json. Stored as double — SQLite has no real decimal.</summary>
    public double? AverageRating { get; set; }

    public int? RatingsCount { get; set; }

    /// <summary>Local, half-star steps 0.5-5.0. Stored as double — SQLite maps decimal to TEXT and breaks ordering.</summary>
    public double? MyRating { get; set; }

    public BookStatus Status { get; set; } = BookStatus.None;

    /// <summary>Orthogonal to Status — hides the whole work from default views.</summary>
    public bool IsIgnored { get; set; }

    public DateTime? DateStarted { get; set; }

    public DateTime? DateRead { get; set; }

    /// <summary>Which edition was actually read/listened to. DeleteBehavior.Restrict — see BookTrakContext.</summary>
    public int? ReadEditionId { get; set; }

    public Edition? ReadEdition { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public DateTime? LastSyncedUtc { get; set; }

    /// <summary>Supplies the displayed cover and edition-level fields. DeleteBehavior.Restrict — see BookTrakContext.</summary>
    public int? PreferredEditionId { get; set; }

    public Edition? PreferredEdition { get; set; }

    public List<Edition> Editions { get; set; } = [];

    public List<BookAuthor> BookAuthors { get; set; } = [];

    public List<BookGenre> BookGenres { get; set; } = [];
}
