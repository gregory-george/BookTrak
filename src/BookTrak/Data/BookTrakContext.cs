using BookTrak.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Data;

/// <summary>
/// Created per-operation via <see cref="IDbContextFactory{TContext}"/> — never shared across
/// overlapping Blazor circuit events. This is the single most important EF decision in the app.
/// </summary>
public class BookTrakContext(DbContextOptions<BookTrakContext> options) : DbContext(options)
{
    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<Edition> Editions => Set<Edition>();

    public DbSet<Genre> Genres => Set<Genre>();

    public DbSet<Series> Series => Set<Series>();

    public DbSet<BookAuthor> BookAuthors => Set<BookAuthor>();

    public DbSet<BookGenre> BookGenres => Set<BookGenre>();

    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasIndex(a => a.OpenLibraryId).IsUnique();
        });

        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasIndex(b => b.OpenLibraryWorkId).IsUnique();

            // Book <-> Edition is a reference cycle (Book.PreferredEditionId/ReadEditionId point
            // into its own Editions collection). Restrict avoids EF's multiple-cascade-paths
            // error; deleting a Book cascades to its Editions (the BookId FK below), so the
            // Preferred/Read pointers must be nulled out first if needed.
            entity.HasOne(b => b.PreferredEdition)
                .WithMany()
                .HasForeignKey(b => b.PreferredEditionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.ReadEdition)
                .WithMany()
                .HasForeignKey(b => b.ReadEditionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.Series)
                .WithMany(s => s.Books)
                .HasForeignKey(b => b.SeriesId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Edition>(entity =>
        {
            entity.HasIndex(e => e.OpenLibraryEditionId).IsUnique();

            entity.HasOne(e => e.Book)
                .WithMany(b => b.Editions)
                .HasForeignKey(e => e.BookId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Genre>(entity =>
        {
            entity.HasIndex(g => g.Slug).IsUnique();
        });

        modelBuilder.Entity<BookAuthor>(entity =>
        {
            entity.HasKey(ba => new { ba.BookId, ba.AuthorId });

            entity.HasOne(ba => ba.Book)
                .WithMany(b => b.BookAuthors)
                .HasForeignKey(ba => ba.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ba => ba.Author)
                .WithMany(a => a.BookAuthors)
                .HasForeignKey(ba => ba.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookGenre>(entity =>
        {
            entity.HasKey(bg => new { bg.BookId, bg.GenreId });

            entity.HasOne(bg => bg.Book)
                .WithMany(b => b.BookGenres)
                .HasForeignKey(bg => bg.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(bg => bg.Genre)
                .WithMany(g => g.BookGenres)
                .HasForeignKey(bg => bg.GenreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.HasKey(s => s.Key);
        });
    }
}
