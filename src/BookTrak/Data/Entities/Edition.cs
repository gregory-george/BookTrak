namespace BookTrak.Data.Entities;

/// <summary>A specific printing of a Book. An audiobook is just an Edition with
/// Format = Audiobook — no separate copies table.</summary>
public class Edition
{
    public int Id { get; set; }

    /// <summary>Open Library edition key, e.g. "OL123M". Null for manually-entered/audnexus-only editions.</summary>
    public string? OpenLibraryEditionId { get; set; }

    public int BookId { get; set; }

    public Book Book { get; set; } = null!;

    public EditionFormat Format { get; set; }

    public string? Isbn10 { get; set; }

    public string? Isbn13 { get; set; }

    /// <summary>Audible ASIN — the audnexus lookup key. Audiobook-only.</summary>
    public string? Asin { get; set; }

    public int? NumberOfPages { get; set; }

    public string? Language { get; set; }

    public string? Publisher { get; set; }

    /// <summary>Free text — never parse as DateTime.</summary>
    public string? PublishDate { get; set; }

    /// <summary>Audiobook-only.</summary>
    public string? Narrator { get; set; }

    /// <summary>Audiobook-only. audnexus returns minutes; store as seconds, display hh:mm:ss.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Audiobook-only.</summary>
    public string? AudioPublisher { get; set; }

    public string? CoverId { get; set; }

    public string? CoverPath { get; set; }

    /// <summary>Orthogonal to Book.IsIgnored — hides this specific printing while keeping the work.</summary>
    public bool IsIgnored { get; set; }
}
