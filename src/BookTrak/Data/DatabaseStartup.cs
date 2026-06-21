using BookTrak.Hosting;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Data;

/// <summary>
/// Startup sequence: back up the existing DB (+ config.json) BEFORE migrating, then migrate,
/// then prune old backups. Backing up first means a bad migration never destroys the only copy.
/// </summary>
internal static class DatabaseStartup
{
    private const int MaxBackups = 10;

    public static void BackupAndMigrate(IDbContextFactory<BookTrakContext> contextFactory)
    {
        BackupIfExists();

        using var context = contextFactory.CreateDbContext();
        context.Database.Migrate();

        PruneBackups();
    }

    private static void BackupIfExists()
    {
        if (!File.Exists(AppPaths.DatabaseFile))
        {
            return; // first run — nothing to back up yet, Migrate() will create the db
        }

        Directory.CreateDirectory(AppPaths.BackupsDirectory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");

        File.Copy(AppPaths.DatabaseFile, Path.Combine(AppPaths.BackupsDirectory, $"BookTrak_{timestamp}.db"), overwrite: false);

        if (File.Exists(AppPaths.ConfigFile))
        {
            File.Copy(AppPaths.ConfigFile, Path.Combine(AppPaths.BackupsDirectory, $"config_{timestamp}.json"), overwrite: false);
        }
    }

    private static void PruneBackups()
    {
        if (!Directory.Exists(AppPaths.BackupsDirectory))
        {
            return;
        }

        PruneByPattern("BookTrak_*.db");
        PruneByPattern("config_*.json");
    }

    private static void PruneByPattern(string searchPattern)
    {
        // Timestamp-suffixed filenames sort chronologically as strings.
        var stale = Directory.GetFiles(AppPaths.BackupsDirectory, searchPattern)
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .Skip(MaxBackups);

        foreach (var file in stale)
        {
            File.Delete(file);
        }
    }
}
