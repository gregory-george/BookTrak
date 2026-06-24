using BookTrak.Hosting;
using Microsoft.EntityFrameworkCore;

namespace BookTrak.Data;

/// <summary>
/// Startup sequence: back up the existing DB (+ config.json) BEFORE migrating, then migrate,
/// then prune old backups. At most one backup is created per calendar day. Backing up first means
/// a bad migration never destroys the only copy.
/// </summary>
internal static class DatabaseStartup
{
    private const int MaxBackups = 10;

    public static void BackupAndMigrate(IDbContextFactory<BookTrakContext> contextFactory)
    {
        CreateBackup();

        using var context = contextFactory.CreateDbContext();
        context.Database.Migrate();
    }

    /// <summary>Copies BookTrak.db (+ config.json) to backups/ with a yyyyMMdd suffix and prunes
    /// anything past the last 10. At most one backup is created per calendar day. Shared by the
    /// startup backup-then-migrate sequence and the manual "Back up now" settings-page button.
    /// Returns false if there's no database yet or today's backup already exists.</summary>
    public static bool CreateBackup()
    {
        if (!File.Exists(AppPaths.DatabaseFile))
        {
            return false; // first run — nothing to back up yet, Migrate() will create the db
        }

        Directory.CreateDirectory(AppPaths.BackupsDirectory);
        var datestamp = DateTime.UtcNow.ToString("yyyyMMdd");

        var dbDest = Path.Combine(AppPaths.BackupsDirectory, $"BookTrak_{datestamp}.db");
        if (File.Exists(dbDest))
        {
            return false; // already backed up today
        }

        File.Copy(AppPaths.DatabaseFile, dbDest, overwrite: false);

        if (File.Exists(AppPaths.ConfigFile))
        {
            File.Copy(AppPaths.ConfigFile, Path.Combine(AppPaths.BackupsDirectory, $"config_{datestamp}.json"), overwrite: false);
        }

        PruneBackups();
        return true;
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
