namespace BookTrak.Data.Entities;

/// <summary>Local, curated taxonomy — distinct from the raw, uncontrolled Open Library
/// subjects stored on <see cref="Book.Subjects"/>.</summary>
public class Genre
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public List<BookGenre> BookGenres { get; set; } = [];
}
