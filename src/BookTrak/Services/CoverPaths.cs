namespace BookTrak.Services;

/// <summary>Covers are stored in the DB as app-relative paths (e.g. "covers/books/123-M.jpg")
/// and served by Program.cs's static file mapping at the "/covers" request path.</summary>
public static class CoverPaths
{
    public static string? ToWebPath(string? coverPath) =>
        string.IsNullOrWhiteSpace(coverPath) ? null : "/" + coverPath.Replace('\\', '/').TrimStart('/');
}
