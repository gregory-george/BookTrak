namespace BookTrak.Hosting;

/// <summary>
/// All BookTrak files live relative to the executable so the app stays portable.
/// </summary>
internal static class AppPaths
{
    public static string RootDirectory { get; } = AppContext.BaseDirectory;

    public static string ConfigFile => Path.Combine(RootDirectory, "config.json");

    public static string LockFile => Path.Combine(RootDirectory, "BookTrak.lock");

    public static string DatabaseFile => Path.Combine(RootDirectory, "BookTrak.db");

    public static string CoversDirectory => Path.Combine(RootDirectory, "covers");

    public static string BackupsDirectory => Path.Combine(RootDirectory, "backups");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Path.Combine(CoversDirectory, "books"));
        Directory.CreateDirectory(Path.Combine(CoversDirectory, "authors"));
        Directory.CreateDirectory(BackupsDirectory);
    }
}
