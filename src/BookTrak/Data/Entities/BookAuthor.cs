namespace BookTrak.Data.Entities;

/// <summary>The only way Book and Author relate — there is no scalar AuthorId on Book,
/// since Open Library works can have multiple authors.</summary>
public class BookAuthor
{
    public int BookId { get; set; }

    public Book Book { get; set; } = null!;

    public int AuthorId { get; set; }

    public Author Author { get; set; } = null!;
}
