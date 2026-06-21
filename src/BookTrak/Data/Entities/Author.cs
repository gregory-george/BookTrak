namespace BookTrak.Data.Entities;

public class Author
{
    public int Id { get; set; }

    /// <summary>Open Library author key, e.g. "OL123A". Null for manually-entered authors.</summary>
    public string? OpenLibraryId { get; set; }

    public required string Name { get; set; }

    public string? PersonalName { get; set; }

    /// <summary>JSON array of alternate names.</summary>
    public string? AlternateNames { get; set; }

    /// <summary>Normalized plain text — OL returns string or {type, value}; normalize on ingest.</summary>
    public string? Bio { get; set; }

    /// <summary>Free text, e.g. "1947" or "circa 1800" — never parse as DateTime.</summary>
    public string? BirthDate { get; set; }

    /// <summary>Free text — never parse as DateTime.</summary>
    public string? DeathDate { get; set; }

    public string? PhotoId { get; set; }

    public string? PhotoPath { get; set; }

    /// <summary>JSON array of external links.</summary>
    public string? Links { get; set; }

    public string? Wikipedia { get; set; }

    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public DateTime? LastSyncedUtc { get; set; }

    public List<BookAuthor> BookAuthors { get; set; } = [];
}
