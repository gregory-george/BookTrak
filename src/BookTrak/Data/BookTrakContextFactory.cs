using BookTrak.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookTrak.Data;

/// <summary>Used only by `dotnet ef` design-time tooling (migrations) — the app itself
/// resolves <see cref="BookTrakContext"/> through <see cref="IDbContextFactory{TContext}"/>
/// registered in Program.cs.</summary>
internal sealed class BookTrakContextFactory : IDesignTimeDbContextFactory<BookTrakContext>
{
    public BookTrakContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BookTrakContext>();
        optionsBuilder.UseSqlite($"Data Source={AppPaths.DatabaseFile}");
        return new BookTrakContext(optionsBuilder.Options);
    }
}
