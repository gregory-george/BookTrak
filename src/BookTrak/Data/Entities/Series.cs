namespace BookTrak.Data.Entities;

/// <summary>Source: audnexus for audiobooks (returns series + position), manual otherwise —
/// Open Library's own series data is too patchy to rely on. Powers "next in series" / missing-volumes.</summary>
public class Series
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? OpenLibrarySeriesKey { get; set; }

    public List<Book> Books { get; set; } = [];
}
