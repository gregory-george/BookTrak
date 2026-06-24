using BookTrak.Data;
using BookTrak.Hosting;
using BookTrak.OpenLibrary;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Services;

/// <summary>Backs the Settings page's manual maintenance actions — "Back up now" and "Clean up
/// covers" — reusing the same logic as the automatic startup sweeps.</summary>
public interface IMaintenanceService
{
    Task<bool> BackupNowAsync(CancellationToken cancellationToken = default);

    Task<int> CleanUpCoversAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches covers for editions/author photos that have an Open Library identifier but
    /// no cached image on disk (never fetched, or the file was deleted). Returns the number of
    /// covers successfully downloaded. Cover fetches that fail are left for a later retry.</summary>
    Task<int> DownloadMissingCoversAsync(CancellationToken cancellationToken = default);
}

internal sealed class MaintenanceService(
    IDbContextFactory<BookTrakContext> contextFactory,
    ICoverCacheService coverCache) : IMaintenanceService
{
    public Task<bool> BackupNowAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DatabaseStartup.CreateBackup());

    public Task<int> CleanUpCoversAsync(CancellationToken cancellationToken = default) =>
        OrphanCoverCleanup.SweepAsync(contextFactory, cancellationToken);

    public async Task<int> DownloadMissingCoversAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var downloaded = 0;

        var editions = await context.Editions
            .Where(e => e.CoverId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var edition in editions)
        {
            if (HasCachedFile(edition.CoverPath))
            {
                continue;
            }

            var path = ToRelativePath(await coverCache
                .GetBookCoverPathAsync(edition.CoverId!, CoverSize.Medium, cancellationToken)
                .ConfigureAwait(false));

            if (path is not null)
            {
                edition.CoverPath = path;
                downloaded++;
            }
        }

        var authors = await context.Authors
            .Where(a => a.PhotoId != null && a.OpenLibraryId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var author in authors)
        {
            if (HasCachedFile(author.PhotoPath))
            {
                continue;
            }

            var path = ToRelativePath(await coverCache
                .GetAuthorPhotoPathAsync(author.OpenLibraryId!, CoverSize.Medium, cancellationToken)
                .ConfigureAwait(false));

            if (path is not null)
            {
                author.PhotoPath = path;
                downloaded++;
            }
        }

        if (downloaded > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return downloaded;
    }

    /// <summary>CoverPath/PhotoPath are stored relative to the app root; resolve and confirm the
    /// file is actually present, since a stale path can outlive its deleted image.</summary>
    private static bool HasCachedFile(string? relativePath) =>
        !string.IsNullOrEmpty(relativePath) && File.Exists(Path.Combine(AppPaths.RootDirectory, relativePath));

    private static string? ToRelativePath(string? absolutePath) =>
        absolutePath is null ? null : Path.GetRelativePath(AppPaths.RootDirectory, absolutePath);
}
