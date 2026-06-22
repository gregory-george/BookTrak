using BookTrak.Hosting;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Data;

/// <summary>Deleting a book/edition/author leaves cover files behind in covers/ — this sweeps
/// covers/books and covers/authors and deletes any file no longer referenced by an
/// Edition.CoverPath or Author.PhotoPath row. Run at startup and exposed as a manual action
/// ("Clean up covers") via IMaintenanceService.</summary>
internal static class OrphanCoverCleanup
{
    public static async Task<int> SweepAsync(IDbContextFactory<BookTrakContext> contextFactory, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        referenced.UnionWith(await context.Editions.Where(e => e.CoverPath != null).Select(e => e.CoverPath!).ToListAsync(cancellationToken).ConfigureAwait(false));
        referenced.UnionWith(await context.Authors.Where(a => a.PhotoPath != null).Select(a => a.PhotoPath!).ToListAsync(cancellationToken).ConfigureAwait(false));

        var deleted = 0;
        foreach (var subDir in new[] { "books", "authors" })
        {
            var folder = Path.Combine(AppPaths.CoversDirectory, subDir);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(folder))
            {
                var relativePath = Path.GetRelativePath(AppPaths.RootDirectory, file);
                if (referenced.Contains(relativePath))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException)
                {
                    // File may be mid-write by a concurrent cover fetch — skip, next sweep will catch it.
                }
            }
        }

        return deleted;
    }
}
